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

        private TcpListener m_HttpListener;
        private TcpListener m_HttpsListener;
        private Thread m_HttpAcceptThread;
        private Thread m_HttpsAcceptThread;
        private X509Certificate2 m_ServerCertificate;
        private bool m_StopRequested;

        public void Start(int httpPort, int httpsPort, X509Certificate2 serverCertificate)
        {
            m_ServerCertificate = serverCertificate;

            m_HttpListener = new TcpListener(IPAddress.Loopback, httpPort);
            m_HttpListener.Start();
            Log.Info("Flatline HTTP listening on http://localhost:" + httpPort + "/");

            m_HttpAcceptThread = new Thread(AcceptHttpLoop);
            m_HttpAcceptThread.IsBackground = false;
            m_HttpAcceptThread.Start();

            if (m_ServerCertificate != null)
            {
                m_HttpsListener = new TcpListener(IPAddress.Loopback, httpsPort);
                m_HttpsListener.Start();
                Log.Info("Flatline HTTPS listening on https://localhost:" + httpsPort + "/");

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
            try
            {
                client.ReceiveTimeout = 15000;
                client.SendTimeout = 15000;

                Stream networkStream = client.GetStream();
                SslStream sslStream = null;

                if (state.UseTls)
                {
                    sslStream = new SslStream(networkStream, false);
                    try
                    {
                        sslStream.AuthenticateAsServer(state.ServerCertificate, false, SslProtocols.Tls12 | SslProtocols.Tls13, false);
                    }
                    catch (Exception tlsException)
                    {
                        bool isSpeculativeProbe = false;
                        for (Exception walker = tlsException; walker != null; walker = walker.InnerException)
                        {
                            if (walker.HResult == SecECertUnknownHResult)
                            {
                                isSpeculativeProbe = true;
                                break;
                            }
                        }
                        if (!isSpeculativeProbe)
                        {
                            Log.Warning("TLS handshake failed: " + tlsException.GetType().Name + ": " + tlsException.Message);
                        }
                        sslStream.Dispose();
                        return;
                    }
                    networkStream = sslStream;
                }

                DispatchRequest(networkStream, state.UseTls);

                if (sslStream != null)
                {
                    sslStream.Dispose();
                }
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

        private static void DispatchRequest(Stream networkStream, bool isHttps)
        {
            DateTime startTime = DateTime.UtcNow;
            string method = "?";
            string path = "?";
            int statusCode = 0;
            try
            {
                FlatlineHttpRequest request = Http11Parser.ReadRequest(networkStream);
                if (request == null)
                {
                    return;
                }
                method = request.Method;
                path = request.Path;

                FlatlineHttpContext context = new FlatlineHttpContext();
                context.Request = request;
                context.IsHttps = isHttps;

                HttpRouter.Route(context);
                statusCode = context.Response.StatusCode;

                context.Response.WriteTo(networkStream);

                TimeSpan elapsed = DateTime.UtcNow - startTime;
                string scheme = "http";
                if (isHttps)
                {
                    scheme = "https";
                }
                Log.Info(scheme + " " + method + " " + path + " " + statusCode + " " + elapsed.TotalMilliseconds.ToString("F1") + "ms");
            }
            catch (Exception requestException)
            {
                if (IsConnectionLifecycleError(requestException))
                {
                    Log.Warning("Connection closed before request completed (" + method + " " + path + "): " + requestException.GetType().Name + ": " + requestException.Message);
                    return;
                }
                Log.Exception(requestException, "Unhandled error: " + method + " " + path);
                try
                {
                    FlatlineHttpResponse errorResponse = new FlatlineHttpResponse();
                    errorResponse.StatusCode = 500;
                    errorResponse.WriteTo(networkStream);
                }
                catch (Exception writeException)
                {
                    Log.Exception(writeException, "Failed to write 500 response");
                }
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
