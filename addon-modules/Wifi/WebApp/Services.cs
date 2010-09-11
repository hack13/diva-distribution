﻿/**
 * Copyright (c) Crista Lopes (aka Diva). All rights reserved.
 * 
 * Redistribution and use in source and binary forms, with or without modification, 
 * are permitted provided that the following conditions are met:
 * 
 *     * Redistributions of source code must retain the above copyright notice, 
 *       this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright notice, 
 *       this list of conditions and the following disclaimer in the documentation 
 *       and/or other materials provided with the distribution.
 *     * Neither the name of the Organizations nor the names of Individual
 *       Contributors may be used to endorse or promote products derived from 
 *       this software without specific prior written permission.
 * 
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND 
 * ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES 
 * OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL 
 * THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, 
 * EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE 
 * GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED 
 * AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING 
 * NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED 
 * OF THE POSSIBILITY OF SUCH DAMAGE.
 * 
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

using Nini.Config;
using log4net;
using OpenMetaverse;
using Nwc.XmlRpc;

using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Services.Interfaces;
using OpenSim.Services.GridService;
using OpenSim.Services.AvatarService;

using Diva.Wifi.WifiScript;
using Environment = Diva.Wifi.Environment;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;

using Diva.OpenSimServices;

namespace Diva.Wifi
{
    public partial class Services
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private WebApp m_WebApp;
        private SmtpClient m_Client;

        private UserAccountService m_UserAccountService;
        private PasswordAuthenticationService m_AuthenticationService;
        private InventoryService m_InventoryService;
        private IGridService m_GridService;
        private IGridUserService m_GridUserService;
        private IAvatarService m_AvatarService;

        private string m_ServerAdminPassword;

        // Sessions
        //private Dictionary<string, SessionInfo> m_Sessions = new Dictionary<string, SessionInfo>();
        private ExpiringCache<string, SessionInfo> m_Sessions = new ExpiringCache<string, SessionInfo>();

        public Services(IConfigSource config, string configName, WebApp webApp)
        {
            m_log.Debug("[Wifi]: Services Starting...");

            m_WebApp = webApp;

            m_ServerAdminPassword = webApp.RemoteAdminPassword;
            //m_log.DebugFormat("[Services]: RemoteAdminPassword is {0}", m_ServerAdminPassword);

            // Create the necessary services
            m_UserAccountService = new UserAccountService(config);
            m_AuthenticationService = new PasswordAuthenticationService(config);
            m_InventoryService = new InventoryService(config);
            m_GridService = new GridService(config);
            m_GridUserService = new GridUserService(config);
            m_AvatarService = new AvatarService(config);

            // Create the "God" account if it doesn't exist
            CreateGod();

            // Connect to our outgoing mail server for password forgetfulness
            m_Client = new SmtpClient(m_WebApp.SmtpHost, m_WebApp.SmtpPort);
            if (m_WebApp.SmtpPort == 25)
                m_Client.EnableSsl = false;
            else
                m_Client.EnableSsl = true;
            m_Client.Credentials = new NetworkCredential(m_WebApp.SmtpUsername, m_WebApp.SmtpPassword);

        }

        private void CreateGod()
        {
            UserAccount god = m_UserAccountService.GetUserAccount(UUID.Zero, m_WebApp.AdminFirst, m_WebApp.AdminLast);
            if (god == null)
            {
                m_log.DebugFormat("[Wifi]: Administrator account {0} {1} does not exist. Creating it...", m_WebApp.AdminFirst, m_WebApp.AdminLast);
                // Doesn't exist. Create one
                god = new UserAccount(UUID.Zero, m_WebApp.AdminFirst, m_WebApp.AdminLast, m_WebApp.AdminEmail);
                god.UserLevel = 100;
                god.UserTitle = "Administrator";
                god.UserFlags = 0;
                SetServiceURLs(god);
                m_UserAccountService.StoreUserAccount(god);
                m_InventoryService.CreateUserInventory(god.PrincipalID);
                if (m_WebApp.AdminPassword == string.Empty)
                    // Signal that the App needs installation
                    m_WebApp.IsInstalled = false;
                else
                {
                    m_AuthenticationService.SetPassword(god.PrincipalID, m_WebApp.AdminPassword);
                    m_WebApp.IsInstalled = true;
                }
            }
            else
            {
                m_log.DebugFormat("[Wifi]: Administrator account {0} {1} exists.", m_WebApp.AdminFirst, m_WebApp.AdminLast);
                // Signal that the App has been previously installed
                m_WebApp.IsInstalled = true;
            }

            if (god.UserLevel < 100)
            {
                // Might have existed but had wrong UserLevel
                god.UserLevel = 100;
                m_UserAccountService.StoreUserAccount(god);
            }

        }

        public bool TryGetSessionInfo(Request request, out SessionInfo sinfo)
        {
            bool success = false;
            sinfo = new SessionInfo();
            if (request.Query.ContainsKey("sid"))
            {
                string sid = request.Query["sid"].ToString();
                if (m_Sessions.Contains(sid))
                {
                    SessionInfo session;
                    if (m_Sessions.TryGetValue(sid, out session) && 
                        session.IpAddress == request.IPEndPoint.Address.ToString())
                    {
                        sinfo = session;
                        m_Sessions.AddOrUpdate(sid, session, DateTime.Now + TimeSpan.FromMinutes(30.0d));
                        success = true;
                    }
                }
            }

            return success;
        }


        // <a href="wifi/..." ...>
        static Regex href = new Regex("(<a\\s+href\\s*=\\s*\\\"(\\S+\\\"))>");
        static Regex action = new Regex("(<form\\s+action\\s*=\\s*\\\"(\\S+\\\")).*>");
        static Regex xmlhttprequest = new Regex("(@@wifi@@(\\S+\\\"))");

        private string PadURLs(Environment env, string sid, string html)
        {
            if ((env.Flags & Flags.IsLoggedIn) == 0)
                return html;

            // The user is logged in
            HashSet<string> uris = new HashSet<string>();
            MatchCollection matches_href = href.Matches(html);
            MatchCollection matches_action = action.Matches(html);
            MatchCollection matches_xmlhttprequest = xmlhttprequest.Matches(html);
            //m_log.DebugFormat("[Wifi]: Matched uris href={0}, action={1}, xmlhttp={2}", matches_href.Count, matches_action.Count, matches_xmlhttprequest.Count);

            foreach (Match match in matches_href)
            {
                // first group is always the total match
                if (match.Groups.Count > 2)
                {
                    string str = match.Groups[1].Value;
                    string uri = match.Groups[2].Value;
                    if (!uri.StartsWith("http") && !uri.EndsWith(".html") && !uri.EndsWith(".css") && !uri.EndsWith(".js"))
                        uris.Add(str);
                }
            }
            foreach (Match match in matches_action)
            {
                // first group is always the total match
                if (match.Groups.Count > 2)
                {
                    string str = match.Groups[1].Value;
                    string uri = match.Groups[2].Value;
                    if (!uri.StartsWith("http") && !uri.EndsWith(".html") && !uri.EndsWith(".css") && !uri.EndsWith(".js"))
                        uris.Add(str);
                }
            }
            foreach (Match match in matches_xmlhttprequest)
            {
                // first group is always the total match
                if (match.Groups.Count > 2)
                {
                    string str = match.Groups[1].Value;
                    string uri = match.Groups[2].Value;
                    if (!uri.StartsWith("http") && !uri.EndsWith(".html") && !uri.EndsWith(".css") && !uri.EndsWith(".js"))
                        uris.Add(str);
                }
            }

            foreach (string uri in uris)
            {
                string uri2 = uri.Substring(0, uri.Length - 1);
                //m_log.DebugFormat("[Wifi]: replacing {0} with {1}", uri, uri2 + "?sid=" + sid + "\"");
                if (!uri.EndsWith("/"))
                    html = html.Replace(uri, uri2 + "/?sid=" + sid + "\"");
                else
                    html = html.Replace(uri, uri2 + "?sid=" + sid + "\"");
            }
            // Remove any @@wifi@@
            html = html.Replace("@@wifi@@", string.Empty);

            return html;
        }

        private void PrintStr(string html)
        {
            foreach (char c in html)
                Console.Write(c);
        }

        private List<object> GetUserList(Environment env, string terms)
        {
            List<UserAccount> accounts = m_UserAccountService.GetUserAccounts(UUID.Zero, terms);
            if (accounts != null && accounts.Count > 0)
            {
                return Objectify<UserAccount>(accounts);
            }
            else
            {
                return new List<object>();
            }

        }

        private List<object> GetActiveUserList(Environment env, string terms)
        {
            List<UserAccount> accounts = m_UserAccountService.GetUserAccounts(UUID.Zero, terms);
            if (accounts != null)
            {
                IEnumerable<UserAccount> nonPending = accounts.Where(user => !user.FirstName.StartsWith("*pending*"));
                return Objectify<UserAccount>(nonPending);
            }
            return new List<object>();
        }

        private List<object> GetDefaultAvatarSelectionList()
        {
            // Present only default avatars of a non-empty type.
            // This allows to specify a default avatar which is used when none is selected
            // during account creation. Use the following configuration settings to enable
            // this ("Default Avatar" may be any avatar name or "" to create an empty
            // standard inventory):
            // [WifiService]
            //    AvatarAccount_ = "Default Avatar"
            //    AvatarPreselection = ""
            IEnumerable<Avatar> visibleAvatars = m_WebApp.DefaultAvatars.Where(avatar => !string.IsNullOrEmpty(avatar.Type));

            return Objectify<Avatar>(visibleAvatars);
        }

        private void SetServiceURLs(UserAccount account)
        {
            account.ServiceURLs = new Dictionary<string, object>();
            account.ServiceURLs["HomeURI"] = m_WebApp.LoginURL.ToString();
            account.ServiceURLs["InventoryServerURI"] = m_WebApp.LoginURL.ToString();
            account.ServiceURLs["AssetServerURI"] = m_WebApp.LoginURL.ToString();
        }

        private List<object> Objectify<T>(IEnumerable<T> listOfThings)
        {
            List<object> listOfObjects = new List<object>();
            foreach (T thing in listOfThings)
                listOfObjects.Add(thing);

            return listOfObjects;
        }

        private void SendEMail(string to, string subject, string message)
        {
            try
            {
                MailMessage msg = new MailMessage();
                msg.From = new MailAddress(m_WebApp.SmtpUsername);
                msg.To.Add(to);
                msg.Subject = "[" + m_WebApp.GridName + "] " + subject;
                msg.Body = message;
                m_Client.SendAsync(msg, to);
            }
            catch (Exception e)
            {
                m_log.WarnFormat("[Wifi]: Exception on sending mail to {0}: {1}", to, e.Message);
            }
        }

        private static void SendCompletedCallback(object sender, AsyncCompletedEventArgs e)
        {
            String token = (string)e.UserState;

            if (e.Cancelled)
                m_log.DebugFormat("[Wifi]: [{0}] Send cancelled.", token);

            if (e.Error != null)
                m_log.DebugFormat("[Wifi]: [{0}] {1}", token, e.Error.ToString());
            else
                m_log.DebugFormat("[Wifi]: Message sent to " + token + ".");
        }

    }

}
