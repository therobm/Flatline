using Flatline.Routes;

namespace Flatline.Http
{
    public static class HttpRouter
    {
        public static void Route(FlatlineHttpContext context)
        {
            string method = context.Request.Method;
            string path = context.Request.Path;

            if (method == "POST" && path == "/api/auth/login")
            {
                AuthRoutes.HandleLogin(context);
                return;
            }
            if (method == "POST" && path == "/api/auth/logout")
            {
                AuthRoutes.HandleLogout(context);
                return;
            }
            if (method == "GET" && path == "/api/auth/session")
            {
                AuthRoutes.HandleGetSession(context);
                return;
            }

            if (method == "GET" && path == "/api/metadata")
            {
                MetadataRoutes.HandleGetMetadata(context);
                return;
            }

            if (method == "POST" && path == "/api/external/bugs")
            {
                ExternalBugRoutes.HandleCreateExternalBug(context);
                return;
            }
            if (method == "GET" && path == "/api/external/bugs")
            {
                ExternalBugRoutes.HandleListExternalBugs(context);
                return;
            }
            long externalBugId = 0;
            if (method == "GET" && TryMatchExternalBugId(path, out externalBugId))
            {
                ExternalBugRoutes.HandleGetExternalBug(context, externalBugId);
                return;
            }
            if (method == "PUT" && TryMatchExternalBugId(path, out externalBugId))
            {
                ExternalBugRoutes.HandleUpdateExternalBug(context, externalBugId);
                return;
            }
            if (method == "POST" && TryMatchExternalBugCommentsPath(path, out externalBugId))
            {
                ExternalBugRoutes.HandleCreateExternalBugComment(context, externalBugId);
                return;
            }
            if (method == "GET" && TryMatchExternalBugCommentsPath(path, out externalBugId))
            {
                ExternalBugRoutes.HandleListExternalBugComments(context, externalBugId);
                return;
            }

            if (method == "GET" && path == "/api/external/projects")
            {
                ExternalProjectRoutes.HandleListExternalProjects(context);
                return;
            }

            if (method == "GET" && path == "/api/api-keys")
            {
                ApiKeyRoutes.HandleListApiKeys(context);
                return;
            }
            if (method == "POST" && path == "/api/api-keys")
            {
                ApiKeyRoutes.HandleCreateApiKey(context);
                return;
            }
            long apiKeyId = 0;
            if (method == "DELETE" && TryMatchApiKeyId(path, out apiKeyId))
            {
                ApiKeyRoutes.HandleDeleteApiKey(context, apiKeyId);
                return;
            }

            if (method == "GET" && path == "/api/bugs")
            {
                BugRoutes.HandleListBugs(context);
                return;
            }
            if (method == "POST" && path == "/api/bugs")
            {
                BugRoutes.HandleCreateBug(context);
                return;
            }
            if (method == "PUT" && path == "/api/bugs/bulk")
            {
                BugRoutes.HandleBulkUpdateBugs(context);
                return;
            }

            long bugId = 0;
            string bugSubPath = "";
            if (TryMatchBugPath(path, out bugId, out bugSubPath))
            {
                if (bugSubPath == "")
                {
                    if (method == "GET")
                    {
                        BugRoutes.HandleGetBug(context, bugId);
                        return;
                    }
                    if (method == "PUT")
                    {
                        BugRoutes.HandleUpdateBug(context, bugId);
                        return;
                    }
                }
                if (bugSubPath == "/comments")
                {
                    if (method == "GET")
                    {
                        CommentRoutes.HandleListComments(context, bugId);
                        return;
                    }
                    if (method == "POST")
                    {
                        CommentRoutes.HandleCreateComment(context, bugId);
                        return;
                    }
                }
                if (bugSubPath == "/related")
                {
                    if (method == "GET")
                    {
                        RelatedBugRoutes.HandleListRelated(context, bugId);
                        return;
                    }
                    if (method == "POST")
                    {
                        RelatedBugRoutes.HandleAddRelated(context, bugId);
                        return;
                    }
                }
                long relatedBugId = 0;
                if (TryMatchRelatedBugId(bugSubPath, out relatedBugId))
                {
                    if (method == "DELETE")
                    {
                        RelatedBugRoutes.HandleDeleteRelated(context, bugId, relatedBugId);
                        return;
                    }
                }
            }

            if (method == "GET" && path == "/api/projects")
            {
                ProjectRoutes.HandleListProjects(context);
                return;
            }
            if (method == "POST" && path == "/api/projects")
            {
                ProjectRoutes.HandleCreateProject(context);
                return;
            }

            long projectId = 0;
            string projectSubPath = "";
            if (TryMatchProjectPath(path, out projectId, out projectSubPath))
            {
                if (projectSubPath == "")
                {
                    if (method == "PUT")
                    {
                        ProjectRoutes.HandleUpdateProject(context, projectId);
                        return;
                    }
                    if (method == "DELETE")
                    {
                        ProjectRoutes.HandleDeleteProject(context, projectId);
                        return;
                    }
                }
                if (projectSubPath == "/versions")
                {
                    if (method == "GET")
                    {
                        VersionRoutes.HandleListVersions(context, projectId);
                        return;
                    }
                    if (method == "POST")
                    {
                        VersionRoutes.HandleCreateVersion(context, projectId);
                        return;
                    }
                }
                long versionId = 0;
                if (TryMatchVersionId(projectSubPath, out versionId))
                {
                    if (method == "PUT")
                    {
                        VersionRoutes.HandleUpdateVersion(context, projectId, versionId);
                        return;
                    }
                    if (method == "DELETE")
                    {
                        VersionRoutes.HandleDeleteVersion(context, projectId, versionId);
                        return;
                    }
                }
            }

            if (method == "GET" && path == "/api/users")
            {
                UserRoutes.HandleListUsers(context);
                return;
            }
            if (method == "POST" && path == "/api/users")
            {
                UserRoutes.HandleCreateUser(context);
                return;
            }

            long userId = 0;
            if (TryMatchUserId(path, out userId))
            {
                if (method == "PUT")
                {
                    UserRoutes.HandleUpdateUser(context, userId);
                    return;
                }
                if (method == "DELETE")
                {
                    UserRoutes.HandleDeleteUser(context, userId);
                    return;
                }
            }

            if (method == "GET" && !path.StartsWith("/api/"))
            {
                StaticFileServer.Serve(context, path);
                return;
            }

            HttpResponseWriter.WriteJson(context, 404, new { error = "Not found" });
        }

        private static bool TryMatchExternalBugId(string path, out long bugId)
        {
            bugId = 0;
            const string prefix = "/api/external/bugs/";
            if (!path.StartsWith(prefix))
            {
                return false;
            }
            string idPart = path.Substring(prefix.Length);
            if (idPart.Contains('/'))
            {
                return false;
            }
            return long.TryParse(idPart, out bugId);
        }

        private static bool TryMatchExternalBugCommentsPath(string path, out long bugId)
        {
            bugId = 0;
            const string prefix = "/api/external/bugs/";
            const string suffix = "/comments";
            if (!path.StartsWith(prefix) || !path.EndsWith(suffix))
            {
                return false;
            }
            int idStart = prefix.Length;
            int idEnd = path.Length - suffix.Length;
            if (idEnd <= idStart)
            {
                return false;
            }
            string idPart = path.Substring(idStart, idEnd - idStart);
            if (idPart.Contains('/'))
            {
                return false;
            }
            return long.TryParse(idPart, out bugId);
        }

        private static bool TryMatchRelatedBugId(string subPath, out long relatedBugId)
        {
            relatedBugId = 0;
            const string prefix = "/related/";
            if (!subPath.StartsWith(prefix))
            {
                return false;
            }
            string idPart = subPath.Substring(prefix.Length);
            if (idPart.Contains('/'))
            {
                return false;
            }
            return long.TryParse(idPart, out relatedBugId);
        }

        private static bool TryMatchBugPath(string path, out long bugId, out string subPath)
        {
            bugId = 0;
            subPath = "";
            const string prefix = "/api/bugs/";
            if (!path.StartsWith(prefix))
            {
                return false;
            }
            string rest = path.Substring(prefix.Length);
            int slashIndex = rest.IndexOf('/');
            string idPart;
            if (slashIndex >= 0)
            {
                idPart = rest.Substring(0, slashIndex);
                subPath = rest.Substring(slashIndex);
            }
            else
            {
                idPart = rest;
            }
            return long.TryParse(idPart, out bugId);
        }

        private static bool TryMatchApiKeyId(string path, out long apiKeyId)
        {
            apiKeyId = 0;
            const string prefix = "/api/api-keys/";
            if (!path.StartsWith(prefix))
            {
                return false;
            }
            string idPart = path.Substring(prefix.Length);
            if (idPart.Contains('/'))
            {
                return false;
            }
            return long.TryParse(idPart, out apiKeyId);
        }

        private static bool TryMatchUserId(string path, out long userId)
        {
            userId = 0;
            const string prefix = "/api/users/";
            if (!path.StartsWith(prefix))
            {
                return false;
            }
            string idPart = path.Substring(prefix.Length);
            if (idPart.Contains('/'))
            {
                return false;
            }
            return long.TryParse(idPart, out userId);
        }

        private static bool TryMatchProjectPath(string path, out long projectId, out string subPath)
        {
            projectId = 0;
            subPath = "";
            const string prefix = "/api/projects/";
            if (!path.StartsWith(prefix))
            {
                return false;
            }
            string rest = path.Substring(prefix.Length);
            int slashIndex = rest.IndexOf('/');
            string idPart;
            if (slashIndex >= 0)
            {
                idPart = rest.Substring(0, slashIndex);
                subPath = rest.Substring(slashIndex);
            }
            else
            {
                idPart = rest;
            }
            return long.TryParse(idPart, out projectId);
        }

        private static bool TryMatchVersionId(string subPath, out long versionId)
        {
            versionId = 0;
            const string prefix = "/versions/";
            if (!subPath.StartsWith(prefix))
            {
                return false;
            }
            string idPart = subPath.Substring(prefix.Length);
            if (idPart.Contains('/'))
            {
                return false;
            }
            return long.TryParse(idPart, out versionId);
        }
    }
}
