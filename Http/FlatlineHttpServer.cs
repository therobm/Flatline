using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using Flatline.Logging;

namespace Flatline.Http
{
    public class FlatlineHttpServer
    {
        private const int SecECertUnknownHResult = unchecked((int)0x80090327);
        private const int TlsHandshakeTimeoutMs = 3000;
        private const int KeepAliveIdleTimeoutMs = 5000;

        private TcpListener m_HttpListener;
        private TcpListener m_HttpsListener;
        private Thread m_HttpAcceptThread;
        private Thread m_HttpsAcceptThread;
        private X509Certificate2 m_ServerCertificate;
        private bool m_StopRequested;

        public void Start(IPAddress bindAddress, int httpPort, int httpsPort, X509Certificate2 serverCertificate)
        {
            m_ServerCertificate = serverCertificate;

            m_HttpListener = new TcpListener(bindAddress, httpPort);
            m_HttpListener.Start();
            Log.Info("Flatline HTTP listening on http://" + bindAddress + ":" + httpPort + "/");

            m_HttpAcceptThread = new Thread(AcceptHttpLoop);
            m_HttpAcceptThread.IsBackground = false;
            m_HttpAcceptThread.Start();

            if (m_ServerCertificate != null)
            {
                m_HttpsListener = new TcpListener(bindAddress, httpsPort);
                m_HttpsListener.Start();
                Log.Info("Flatline HTTPS listening on https://" + bindAddress + ":" + httpsPort + "/");

                m_HttpsAcceptThread = new Thread(AcceptHttpsLoop);
                m_HttpsAcceptThread.IsBackground = false;
                m_HttpsAcceptThread.Start();
            }
        }

        public void StopAndWait()
        {
            m_StopRequested = true;
            if (m_HttpListener != null)
            {
                m_HttpListener.Stop();
            }
            if (m_HttpsListener != null)
            {
                m_HttpsListener.Stop();
            }
            if (m_HttpAcceptThread != null)
            {
                m_HttpAcceptThread.Join();
            }
            if (m_HttpsAcceptThread != null)
            {
                m_HttpsAcceptThread.Join();
            }
        }

        private void AcceptHttpLoop()
        {
            AcceptLoop(m_HttpListener, false);
        }

        private void AcceptHttpsLoop()
        {
            AcceptLoop(m_HttpsListener, true);
        }

        private void AcceptLoop(TcpListener listener, bool useTls)
        {
            while (!m_StopRequested)
            {
                TcpClient client;
                try
                {
                    client = listener.AcceptTcpClient();
                }
                catch (SocketException)
                {
                    if (m_StopRequested)
                    {
                        return;
                    }
                    continue;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                ConnectionState state = new ConnectionState();
                state.Client = client;
                state.UseTls = useTls;
                state.ServerCertificate = m_ServerCertificate;
                ThreadPool.QueueUserWorkItem(HandleConnection, state);
            }
        }

        private static void HandleConnection(object stateObject)
        {
            ConnectionState state = (ConnectionState)stateObject;
            TcpClient client = state.Client;
            string remoteIp = "";
            try
            {
                IPEndPoint remoteEndPoint = client.Client.RemoteEndPoint as IPEndPoint;
                if (remoteEndPoint != null)
                {
                    remoteIp = remoteEndPoint.Address.ToString();
                }
            }
            catch (Exception)
            {
                /* RemoteEndPoint can throw if the socket is already disposed.
                 * Leave remoteIp empty in that case. */
            }
            try
            {
                client.ReceiveTimeout = KeepAliveIdleTimeoutMs;
                client.SendTimeout = 15000;

                Stream networkStream = client.GetStream();
                SslStream sslStream = null;

                if (state.UseTls)
                {
                    sslStream = new SslStream(networkStream, false);
                    try
                    {
                        /*
                         * Bound the handshake to TlsHandshakeTimeoutMs. A client that
                         * opens the TCP connection but never sends a ClientHello (or
                         * trickles bytes) would otherwise hold this dispatch thread
                         * indefinitely. Using the async overload with a CancellationToken
                         * cancels the whole handshake, not just a single read.
                         */
                        CancellationTokenSource handshakeTimeout = new CancellationTokenSource(TlsHandshakeTimeoutMs);
                        try
                        {
                            SslServerAuthenticationOptions handshakeOptions = new SslServerAuthenticationOptions();
                            handshakeOptions.ServerCertificate = state.ServerCertificate;
                            handshakeOptions.ClientCertificateRequired = false;
                            handshakeOptions.EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
                            handshakeOptions.CertificateRevocationCheckMode = X509RevocationMode.NoCheck;
                            sslStream.AuthenticateAsServerAsync(handshakeOptions, handshakeTimeout.Token).GetAwaiter().GetResult();
                        }
                        finally
                        {
                            handshakeTimeout.Dispose();
                        }
                    }
                    catch (Exception tlsException)
                    {
                        bool isSpeculativeProbe = false;
                        bool isTimeout = false;
                        for (Exception walker = tlsException; walker != null; walker = walker.InnerException)
                        {
                            if (walker.HResult == SecECertUnknownHResult)
                            {
                                isSpeculativeProbe = true;
                                break;
                            }
                            if (walker is OperationCanceledException)
                            {
                                isTimeout = true;
                                break;
                            }
                        }
                        if (isTimeout)
                        {
                            Log.Warning("TLS handshake timed out after " + TlsHandshakeTimeoutMs + "ms; closing connection.");
                        }
                        else if (!isSpeculativeProbe)
                        {
                            Log.Warning("TLS handshake failed: " + tlsException.GetType().Name + ": " + tlsException.Message);
                        }
                        sslStream.Dispose();
                        return;
                    }
                    networkStream = sslStream;
                }

                /* Wrap the per-connection stream in a BufferedStream so Http11Parser.ReadLine
                 * stops driving one syscall (or one TLS-decrypt round-trip) per byte. An 8 KB
                 * read buffer is plenty for a single HTTP request's headers; writes also pass
                 * through it but FlatlineHttpResponse.WriteTo already calls Flush() at the end
                 * of each response, so this does not delay responses. */
                BufferedStream bufferedStream = new BufferedStream(networkStream, 8192);
                networkStream = bufferedStream;

                /* Allocated once per connection and reused for every request
                 * line and header line on this keep-alive connection, so the
                 * parser stops minting a fresh MemoryStream per line. */
                byte[] lineBuffer = new byte[Http11Parser.MaxLineLength];

                /*
                 * HTTP/1.1 keep-alive loop. Each iteration reads one request and writes
                 * one response. The loop ends when:
                 *   - the client sent Connection: close,
                 *   - the client closed the TCP connection (parser returns null),
                 *   - the read timeout fires after KeepAliveIdleTimeoutMs of silence,
                 *   - or an unhandled exception forced a 500 response.
                 */
                for (;;)
                {
                    bool shouldContinue = DispatchRequest(networkStream, state.UseTls, remoteIp, lineBuffer);
                    if (!shouldContinue)
                    {
                        break;
                    }
                }

                /* BufferedStream.Dispose disposes the underlying stream too, so this also
                 * tears down the SslStream when TLS was in use. */
                bufferedStream.Dispose();
            }
            catch (Exception connectionException)
            {
                if (IsConnectionLifecycleError(connectionException))
                {
                    Log.Warning("Connection error: " + connectionException.GetType().Name + ": " + connectionException.Message);
                }
                else
                {
                    Log.Exception(connectionException, "Connection error");
                }
            }
            finally
            {
                client.Close();
            }
        }

        /*
         * Reads one request, dispatches it, writes one response. Returns true if the
         * connection should stay open for the next request, false otherwise.
         */
        private static bool DispatchRequest(Stream networkStream, bool isHttps, string remoteIp, byte[] lineBuffer)
        {
            string method = "?";
            string path = "?";
            int statusCode = 0;
            try
            {
                FlatlineHttpRequest request = Http11Parser.ReadRequest(networkStream, lineBuffer);
                if (request == null)
                {
                    /* Client closed the connection cleanly between requests. */
                    return false;
                }

				DateTime startTime = DateTime.UtcNow;
				method = request.Method;
                path = request.Path;

                bool clientWantsClose = false;
                string connectionHeader;
                if (request.Headers.TryGetValue("Connection", out connectionHeader))
                {
                    if (connectionHeader.ToLowerInvariant().Contains("close"))
                    {
                        clientWantsClose = true;
                    }
                }

                FlatlineHttpContext context = new FlatlineHttpContext();
                context.Request = request;
                context.IsHttps = isHttps;
                context.RemoteIpAddress = remoteIp;

                HttpRouter.Route(context);
                statusCode = context.Response.StatusCode;
                context.Response.KeepAlive = !clientWantsClose;
                context.Response.WriteTo(networkStream);

                TimeSpan elapsed = DateTime.UtcNow - startTime;
                string scheme = "http";
                if (isHttps)
                {
                    scheme = "https";
                }
                Log.Info(scheme + " " + method + " " + path + " " + statusCode + " " + elapsed.TotalMilliseconds.ToString("F1") + "ms");

                return !clientWantsClose;
            }
            catch (Exception requestException)
            {
                if (IsConnectionLifecycleError(requestException))
                {
                    /* If method/path were never populated, the connection died before any
                     * request bytes arrived (idle timeout, client disconnect, RST). That's
                     * expected end-of-life for a keep-alive connection — close silently. */
                    if (method != "?")
                    {
                        Log.Warning("Connection closed mid-request (" + method + " " + path + "): " + requestException.GetType().Name + ": " + requestException.Message);
                    }
                    return false;
                }
                Log.Exception(requestException, "Unhandled error: " + method + " " + path);
                try
                {
                    FlatlineHttpResponse errorResponse = new FlatlineHttpResponse();
                    errorResponse.StatusCode = 500;
                    errorResponse.KeepAlive = false;
                    errorResponse.WriteTo(networkStream);
                }
                catch (Exception writeException)
                {
                    Log.Exception(writeException, "Failed to write 500 response");
                }
                return false;
            }
        }

        private static bool IsConnectionLifecycleError(Exception exception)
        {
            for (Exception walker = exception; walker != null; walker = walker.InnerException)
            {
                if (walker is SocketException)
                {
                    return true;
                }
                if (walker is ObjectDisposedException)
                {
                    return true;
                }
            }
            return false;
        }

        private class ConnectionState
        {
            public TcpClient Client;
            public bool UseTls;
            public X509Certificate2 ServerCertificate;
        }
    }
}
