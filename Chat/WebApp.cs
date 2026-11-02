using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace CP_SDK.Chat
{
    /// <summary>
    /// Web app
    /// </summary>
    internal class WebApp
    {
        /// <summary>
        /// Listener
        /// </summary>
        private static HttpListener m_Listener;
        private const int TwitchOAuthCallbackPort = 8339;
        private static HttpListener m_CallbackListener;
        /// <summary>
        /// Listener lifecycle lock
        /// </summary>
        private static readonly object m_ListenerLock = new object();
        /// <summary>
        /// Page data
        /// </summary>
        private static string m_PageData;

        internal static int Port { get; private set; }

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Start the webapp
        /// </summary>
        internal static bool Start()
        {
            lock (m_ListenerLock)
            {
                if (m_Listener != null)
                {
                    if (Port != TwitchOAuthCallbackPort)
                        StartCallbackListener();
                    return true;
                }

                if (m_PageData == null)
                    m_PageData = Misc.Resources.FromRelPathStr(Assembly.GetExecutingAssembly(), "CP_SDK.Chat.Resources.index.html");

                var l_PreferredPort = ChatModSettings.Instance.WebAppPort;
                if (l_PreferredPort < 1 || l_PreferredPort > 65535)
                {
                    ChatPlexSDK.Logger.Warning("[CP_SDK.Chat][WebApp.Start] Invalid configuration port; chat remains available.");
                    return false;
                }

                Exception l_LastException = null;
                for (int l_Attempt = 0; l_Attempt < 8 && l_PreferredPort + l_Attempt <= 65535; l_Attempt++)
                {
                    var l_Port = l_PreferredPort + l_Attempt;
                    var l_Listener = new HttpListener();
                    l_Listener.Prefixes.Add($"http://localhost:{l_Port}/");

                    try
                    {
                        l_Listener.Start();
                    }
                    catch (Exception l_Exception) when (l_Exception is HttpListenerException || l_Exception is SocketException)
                    {
                        l_Listener.Close();
                        l_LastException = l_Exception;
                        if (l_Exception is SocketException l_SocketException && l_SocketException.SocketErrorCode == SocketError.AddressAlreadyInUse
                            || l_Exception is HttpListenerException l_ListenerException && (l_ListenerException.NativeErrorCode == 183 || l_ListenerException.NativeErrorCode == 10048))
                            continue;
                        break;
                    }

                    m_Listener = l_Listener;
                    Port = l_Port;
                    Task.Run(() => Listen(l_Listener));
                    if (l_Port != TwitchOAuthCallbackPort)
                        StartCallbackListener();
                    if (l_Port != l_PreferredPort)
                        ChatPlexSDK.Logger.Info($"[CP_SDK.Chat][WebApp.Start] Configuration port {l_PreferredPort} is busy; using http://localhost:{l_Port}/ for this process.");
                    return true;
                }

                ChatPlexSDK.Logger.Warning("[CP_SDK.Chat][WebApp.Start] Configuration listener is unavailable; chat remains available.");
                ChatPlexSDK.Logger.Warning(l_LastException);
                return false;
            }
        }

        private static void StartCallbackListener()
        {
            if (m_CallbackListener != null)
                return;

            var l_Listener = new HttpListener();
            l_Listener.Prefixes.Add($"http://localhost:{TwitchOAuthCallbackPort}/");

            try
            {
                l_Listener.Start();
            }
            catch (Exception l_Exception) when (l_Exception is HttpListenerException || l_Exception is SocketException)
            {
                l_Listener.Close();
                ChatPlexSDK.Logger.Warning($"[CP_SDK.Chat][WebApp.Start] Twitch OAuth callback listener on port {TwitchOAuthCallbackPort} is unavailable; token acquisition cannot complete.");
                ChatPlexSDK.Logger.Warning(l_Exception);
                return;
            }

            m_CallbackListener = l_Listener;
            Task.Run(() => Listen(l_Listener));
        }

        private static void Listen(HttpListener p_Listener)
        {
            try
            {
                while (p_Listener.IsListening)
                    OnContext(p_Listener.GetContext());
            }
            catch (ObjectDisposedException) { }
            catch (Exception l_Exception)
            {
                lock (m_ListenerLock)
                {
                    if (m_Listener == p_Listener)
                    {
                        ChatPlexSDK.Logger.Error("[CP_SDK.Chat][WebApp.Listen] Configuration listener stopped unexpectedly.");
                        ChatPlexSDK.Logger.Error(l_Exception);
                    }
                    else if (m_CallbackListener == p_Listener)
                    {
                        ChatPlexSDK.Logger.Error("[CP_SDK.Chat][WebApp.Listen] Twitch OAuth callback listener stopped unexpectedly.");
                        ChatPlexSDK.Logger.Error(l_Exception);
                    }
                }
            }
            finally
            {
                lock (m_ListenerLock)
                {
                    if (m_Listener == p_Listener)
                    {
                        m_Listener = null;
                        Port = 0;
                    }
                    if (m_CallbackListener == p_Listener)
                        m_CallbackListener = null;
                    p_Listener.Close();
                }
            }
        }
        /// <summary>
        /// Stop the webapp
        /// </summary>
        internal static void Stop()
        {
            lock (m_ListenerLock)
            {
                var l_Listener = m_Listener;
                var l_CallbackListener = m_CallbackListener;
                if (l_Listener == null && l_CallbackListener == null)
                    return;

                m_Listener = null;
                m_CallbackListener = null;
                Port = 0;
                l_Listener?.Close();
                l_CallbackListener?.Close();
                ChatPlexSDK.Logger.Info("[CP_SDK.Chat][WebApp.Stop] Stopped");
            }
        }

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// On HTTP request
        /// </summary>
        /// <param name="p_Context"></param>
        internal static void OnContext(HttpListenerContext p_Context)
        {
            try
            {
                var l_Request       = p_Context.Request;
                var l_Response      = p_Context.Response;

                if (l_Request.HttpMethod == "POST" && l_Request.Url.AbsolutePath == "/submit")
                {
                    using (var l_Reader = new StreamReader(l_Request.InputStream, l_Request.ContentEncoding))
                    {
                        var l_PostData = l_Reader.ReadToEnd();
                        var l_PostDict = new Dictionary<string, string>();

                        ChatModSettings.Instance.LaunchWebAppOnStartup = false;

                        foreach (var l_CurrentPostData in l_PostData.Split('&'))
                        {
                            try
                            {
                                var l_Split = l_CurrentPostData.Split('=');

                                switch (l_Split[0])
                                {
                                    case "start_webapp":
                                        var l_PostValue = l_Split[1].ToLower().Trim();
                                        ChatModSettings.Instance.LaunchWebAppOnStartup = l_PostValue == "true" || l_PostValue == "on" || l_PostValue == "1";
                                        break;

                                    default:
                                        l_PostDict.Add(l_Split[0], l_Split[1]);
                                        break;
                                }
                            }
                            catch (Exception l_Exception)
                            {
                                ChatPlexSDK.Logger.Error("[CP_SDK.Chat][WebApp.OnContext] An exception occurred in OnLoginDataUpdated callback");
                                ChatPlexSDK.Logger.Error(l_Exception);
                            }
                        }

                        ChatModSettings.Instance.Save();

                        foreach (var l_Service in Service.Multiplexer?.Services)
                            l_Service.WebPageOnPost(l_PostDict);
                    }

                    l_Response.Redirect("/#saved");
                    l_Response.Close();

                    return;
                }

                var l_PageBuilder = new StringBuilder(m_PageData);

                l_PageBuilder.Replace("{APPLICATION_NAME}", ChatPlexSDK.ProductName + "Plus");
                l_PageBuilder.Replace("{START_WEBAPP}",     ChatModSettings.Instance.LaunchWebAppOnStartup ? "checked" : "");

                var l_HTMLForm       = "";
                var l_HTML           = "";
                var l_JS             = "";
                var l_JS_VALIDATE    = "";
                foreach (var l_Service in Service.Multiplexer?.Services)
                {
                    var l_HTMLFormToAdd     = l_Service.WebPageHTMLForm();
                    var l_HTMLToAdd         = l_Service.WebPageHTML();
                    var l_JSToAdd           = l_Service.WebPageJS();
                    var l_JSValidateToAdd   = l_Service.WebPageJSValidate();

                    if (!string.IsNullOrEmpty(l_HTMLFormToAdd))     l_HTMLForm      += l_HTMLFormToAdd      + "<br/>";
                    if (!string.IsNullOrEmpty(l_HTMLToAdd))         l_HTML          += l_HTMLToAdd          + "<br/>";
                    if (!string.IsNullOrEmpty(l_JSToAdd))           l_JS            += l_JSToAdd            + "\n";
                    if (!string.IsNullOrEmpty(l_JSValidateToAdd))   l_JS_VALIDATE   += l_JSValidateToAdd    + "\n";
                }

                l_PageBuilder.Replace("{_HTML_FORM_}",      l_HTMLForm);
                l_PageBuilder.Replace("{_HTML_}",           l_HTML);
                l_PageBuilder.Replace("{_SERVICES_COUNT_}", Service.Multiplexer?.Services?.Count.ToString());
                l_PageBuilder.Replace("{_JS_}",             l_JS);
                l_PageBuilder.Replace("{_JS_VALIDATE_}",    l_JS_VALIDATE);

                byte[] l_Data = Encoding.UTF8.GetBytes(l_PageBuilder.ToString());
                l_Response.ContentType      = "text/html";
                l_Response.ContentEncoding  = Encoding.UTF8;
                l_Response.ContentLength64  = l_Data.LongLength;

                l_Response.OutputStream.Write(l_Data, 0, l_Data.Length);
                l_Response.OutputStream.Close();
            }
            catch (Exception l_Exception)
            {
                ChatPlexSDK.Logger.Error("[CP_SDK.Chat][WebApp.OnContext] Exception occurred during webapp request.");
                ChatPlexSDK.Logger.Error(l_Exception);
            }
        }
    }
}
