/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSim Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections;
using System.Net;
using System.IO;
using System.Timers;
using libsecondlife;
using Mono.Addins;
using Nwc.XmlRpc;
using OpenSim.Framework;
using OpenSim.Framework.Console;
using OpenSim.Framework.Servers;
using OpenSim.Region.Environment.Scenes;

[assembly : Addin]
[assembly : AddinDependency("OpenSim", "0.5")]

namespace OpenSim.ApplicationPlugins.LoadRegions
{
    [Extension("/OpenSim/Startup")]
    public class RemoteAdminPlugin : IApplicationPlugin
    {
        private static readonly log4net.ILog m_log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private OpenSimMain m_app;
        private BaseHttpServer m_httpd;
        private string requiredPassword = String.Empty;

        public void Initialise(OpenSimMain openSim)
        {
            try
            {
                if (openSim.ConfigSource.Configs["RemoteAdmin"] != null && openSim.ConfigSource.Configs["RemoteAdmin"].GetBoolean("enabled", false))
                {
                    m_log.Info("[RADMIN]: Remote Admin Plugin Enabled");
                    requiredPassword = openSim.ConfigSource.Configs["RemoteAdmin"].GetString("access_password", String.Empty);

                    m_app = openSim;
                    m_httpd = openSim.HttpServer;

                    m_httpd.AddXmlRPCHandler("admin_create_region", XmlRpcCreateRegionMethod);
                    m_httpd.AddXmlRPCHandler("admin_shutdown", XmlRpcShutdownMethod);
                    m_httpd.AddXmlRPCHandler("admin_broadcast", XmlRpcAlertMethod);
                    m_httpd.AddXmlRPCHandler("admin_restart", XmlRpcRestartMethod);
                    m_httpd.AddXmlRPCHandler("admin_load_heightmap", XmlRpcLoadHeightmapMethod);
                    m_httpd.AddXmlRPCHandler("admin_create_user", XmlRpcCreateUserMethod);
                    m_httpd.AddXmlRPCHandler("admin_load_xml", XmlRpcLoadXMLMethod);
                }
            }
            catch (NullReferenceException)
            {
                // Ignore.
            }
        }

        public XmlRpcResponse XmlRpcRestartMethod(XmlRpcRequest request)
        {
            XmlRpcResponse response = new XmlRpcResponse();
            Hashtable requestData = (Hashtable) request.Params[0];

            LLUUID regionID = new LLUUID((string) requestData["regionID"]);

            Hashtable responseData = new Hashtable();
            if (requiredPassword != String.Empty &&
                (!requestData.Contains("password") || (string) requestData["password"] != requiredPassword))
            {
                responseData["accepted"] = "false";
                response.Value = responseData;
            }
            else
            {
                responseData["accepted"] = "true";
                response.Value = responseData;

                Scene RebootedScene;

                if (m_app.SceneManager.TryGetScene(regionID, out RebootedScene))
                {
                    responseData["rebooting"] = "true";
                    RebootedScene.Restart(30);
                }
                else
                {
                    responseData["rebooting"] = "false";
                }
            }

            return response;
        }

        public XmlRpcResponse XmlRpcAlertMethod(XmlRpcRequest request)
        {
            XmlRpcResponse response = new XmlRpcResponse();
            Hashtable requestData = (Hashtable) request.Params[0];

            Hashtable responseData = new Hashtable();
            if (requiredPassword != String.Empty &&
                (!requestData.Contains("password") || (string) requestData["password"] != requiredPassword))
            {
                responseData["accepted"] = "false";
                response.Value = responseData;
            }
            else
            {
                string message = (string) requestData["message"];
                m_log.Info("[RADMIN]: Broadcasting: " + message);

                responseData["accepted"] = "true";
                response.Value = responseData;

                m_app.SceneManager.SendGeneralMessage(message);
            }

            return response;
        }

        public XmlRpcResponse XmlRpcLoadHeightmapMethod(XmlRpcRequest request)
        {
            XmlRpcResponse response = new XmlRpcResponse();
            Hashtable requestData = (Hashtable)request.Params[0];

            Hashtable responseData = new Hashtable();
            if (requiredPassword != String.Empty &&
                (!requestData.Contains("password") || (string)requestData["password"] != requiredPassword))
            {
                responseData["accepted"] = "false";
                response.Value = responseData;
            }
            else
            {
                string file = (string)requestData["filename"];
                LLUUID regionID = LLUUID.Parse((string)requestData["regionid"]);
                m_log.Info("[RADMIN]: Terrain Loading: " + file);

                responseData["accepted"] = "true";

                Scene region = null;

                if (m_app.SceneManager.TryGetScene(regionID, out region))
                {
                    //region.LoadWorldMap(file);
                    responseData["success"] = "true";
                }
                else
                {
                    responseData["success"] = "false";
                    responseData["error"] = "1: Unable to get a scene with that name.";
                }
                response.Value = responseData;
            }

            return response;
        }

        public XmlRpcResponse XmlRpcShutdownMethod(XmlRpcRequest request)
        {
            m_log.Info("[RADMIN]: Received Shutdown Administrator Request");
            XmlRpcResponse response = new XmlRpcResponse();
            Hashtable requestData = (Hashtable) request.Params[0];
            Hashtable responseData = new Hashtable();
            if (requiredPassword != String.Empty &&
                (!requestData.Contains("password") || (string) requestData["password"] != requiredPassword))
            {
                responseData["accepted"] = "false";
                response.Value = responseData;
            }
            else
            {
                if ((string) requestData["shutdown"] == "delayed")
                {
                    int timeout = (Int32) requestData["milliseconds"];

                    responseData["accepted"] = "true";
                    response.Value = responseData;

                    m_app.SceneManager.SendGeneralMessage("Region is going down in " + ((int) (timeout/1000)).ToString() +
                                                          " second(s). Please save what you are doing and log out.");

                    // Perform shutdown
                    Timer shutdownTimer = new Timer(timeout); // Wait before firing
                    shutdownTimer.AutoReset = false;
                    shutdownTimer.Elapsed += new ElapsedEventHandler(shutdownTimer_Elapsed);
                    shutdownTimer.Start();

                    return response;
                }
                else
                {
                    responseData["accepted"] = "true";
                    response.Value = responseData;

                    m_app.SceneManager.SendGeneralMessage("Region is going down now.");

                    // Perform shutdown
                    Timer shutdownTimer = new Timer(2000); // Wait 2 seconds before firing
                    shutdownTimer.AutoReset = false;
                    shutdownTimer.Elapsed += new ElapsedEventHandler(shutdownTimer_Elapsed);
                    shutdownTimer.Start();

                    return response;
                }
            }
            return response;
        }

        private void shutdownTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            m_app.Shutdown();
        }

        /// <summary>
        /// Create a new region.
        /// <summary>
        /// <param name="request">incoming XML RPC request</param>
        /// <remarks>
        /// XmlRpcCreateRegionMethod takes the following XMLRPC
        /// parameters
        /// <list type="table">
        /// <listheader><term>parameter name</term><description>description</description></listheader>
        /// <item><term>password</term>
        ///       <description>admin password as set in OpenSim.ini</description></item>
        /// <item><term>region_name</term>
        ///       <description>desired region name</description></item>
        /// <item><term>region_id</term>
        ///       <description>(optional) desired region UUID</description></item>
        /// <item><term>region_x</term>
        ///       <description>desired region X coordinate</description></item>
        /// <item><term>region_y</term>
        ///       <description>desired region Y coordinate</description></item>
        /// <item><term>region_master_first</term>
        ///       <description>firstname of region master</description></item>
        /// <item><term>region_master_last</term>
        ///       <description>lastname of region master</description></item>
        /// <item><term>listen_ip</term>
        ///       <description>internal IP address</description></item>
        /// <item><term>listen_port</term>
        ///       <description>internal port</description></item>
        /// <item><term>external_address</term>
        ///       <description>external IP address</description></item>
        /// <item><term>datastore</term>
        ///       <description>datastore parameter (?)</description></item>
        /// </list>
        /// 
        /// XmlRpcCreateRegionMethod returns
        /// <list type="table">
        /// <listheader><term>name</term><description>description</description></listheader>
        /// <item><term>success</term>
        ///       <description>true or false</description></item>
        /// <item><term>error</term>
        ///       <description>error message if success is false</description></item>
        /// <item><term>region_uuid</term>
        ///       <description>UUID of the newly created region</description></item>
        /// <item><term>region_name</term>
        ///       <description>name of the newly created region</description></item>
        /// </list>
        /// </remarks>
        public XmlRpcResponse XmlRpcCreateRegionMethod(XmlRpcRequest request)
        {
            m_log.Info("[RADMIN]: Received Create Region Administrator Request");
            XmlRpcResponse response = new XmlRpcResponse();
            Hashtable requestData = (Hashtable) request.Params[0];
            Hashtable responseData = new Hashtable();

            try {
                // check completeness
                foreach (string p in new string[] { "password", 
                                                    "region_name", "region_x", "region_y", 
                                                    "region_master_first", "region_master_last",
                                                    "listen_ip", "listen_port", "external_address"})
                {
                    if (!requestData.Contains(p)) 
                        throw new Exception(String.Format("missing parameter {0}", p));
                }

                // check password
                if (!String.IsNullOrEmpty(requiredPassword) &&
                    (string)requestData["password"] != requiredPassword) throw new Exception("wrong password");

                // bool persist = Convert.ToBoolean((string)requestData["persist"]);
                RegionInfo region = null;
                // if (!persist) 
                // {
                //     region = new RegionInfo();
                // }
                // else 
                // {
                //     region = new RegionInfo("DEFAULT REGION CONFIG", 
                //                                    Path.Combine(regionConfigPath, "default.xml"), false);
                // }
                region = new RegionInfo();

                
                if (requestData.ContainsKey("region_id") && 
                    !String.IsNullOrEmpty((string) requestData["region_id"])) 
                {
                    // FIXME: need to check whether region_id already
                    // in use
                    region.RegionID = (string) requestData["region_id"];
                } 
                else 
                {
                    region.RegionID = LLUUID.Random();
                }

                // FIXME: need to check whether region_name already
                // in use
                region.RegionName = (string) requestData["region_name"];
                region.RegionLocX = Convert.ToUInt32((Int32) requestData["region_x"]);
                region.RegionLocY = Convert.ToUInt32((Int32) requestData["region_y"]);
                
                // Security risk
                if (requestData.ContainsKey("datastore"))
                    region.DataStore = (string) requestData["datastore"];

                region.InternalEndPoint = 
                    new IPEndPoint(IPAddress.Parse((string) requestData["listen_ip"]), 0);
                
                // FIXME: need to check whether listen_port already in use!
                region.InternalEndPoint.Port = (Int32) requestData["listen_port"];
                region.ExternalHostName = (string) requestData["external_address"];
                    
                region.MasterAvatarFirstName = (string) requestData["region_master_first"];
                region.MasterAvatarLastName = (string) requestData["region_master_last"];
                
                m_app.CreateRegion(region, true);

                responseData["success"]     = "true";
                responseData["region_name"] = region.RegionName;
                responseData["region_uuid"] = region.RegionID.ToString();

                response.Value = responseData;
            }
            catch (Exception e)
            {
                responseData["success"] = "false";
                responseData["error"] = e.Message;

                response.Value = responseData;
            }

            return response;
        }

        /// <summary>
        /// Create a new user account.
        /// <summary>
        /// <param name="request">incoming XML RPC request</param>
        /// <remarks>
        /// XmlRpcCreateUserMethod takes the following XMLRPC
        /// parameters
        /// <list type="table">
        /// <listheader><term>parameter name</term><description>description</description></listheader>
        /// <item><term>password</term>
        ///       <description>admin password as set in OpenSim.ini</description></item>
        /// <item><term>user_firstname</term>
        ///       <description>avatar's first name</description></item>
        /// <item><term>user_lastname</term>
        ///       <description>avatar's last name</description></item>
        /// <item><term>user_password</term>
        ///       <description>avatar's password</description></item>
        /// <item><term>start_region_x</term>
        ///       <description>avatar's start region coordinates, X value</description></item>
        /// <item><term>start_region_y</term>
        ///       <description>avatar's start region coordinates, Y value</description></item>
        /// </list>
        /// 
        /// XmlRpcCreateUserMethod returns
        /// <list type="table">
        /// <listheader><term>name</term><description>description</description></listheader>
        /// <item><term>success</term>
        ///       <description>true or false</description></item>
        /// <item><term>error</term>
        ///       <description>error message if success is false</description></item>
        /// <item><term>avatar_uuid</term>
        ///       <description>UUID of the newly created avatar
        ///                    account; LLUUID.Zero if failed.
        ///       </description></item>
        /// </list>
        /// </remarks>
        public XmlRpcResponse XmlRpcCreateUserMethod(XmlRpcRequest request)
        {
            m_log.Info("[RADMIN]: Received Create User Administrator Request");
            XmlRpcResponse response = new XmlRpcResponse();
            Hashtable requestData = (Hashtable) request.Params[0];
            Hashtable responseData = new Hashtable();

            try 
            {
                // check completeness
                foreach (string p in new string[] { "password", 
                                                    "user_firstname", "user_lastname", "user_password",
                                                    "start_region_x", "start_region_y" })
                {
                    if (!requestData.Contains(p)) 
                        throw new Exception(String.Format("missing parameter {0}", p));
                }

                // check password
                if (!String.IsNullOrEmpty(requiredPassword) &&
                    (string)requestData["password"] != requiredPassword) throw new Exception("wrong password");

                // do the job
                string firstname = (string) requestData["user_firstname"];
                string lastname  = (string) requestData["user_lastname"];
                string passwd    = (string) requestData["user_password"];
                uint   regX      = Convert.ToUInt32((Int32)requestData["start_region_x"]);
                uint   regY      = Convert.ToUInt32((Int32)requestData["start_region_y"]);
                
                // FIXME: need to check whether "firstname lastname"
                // already exists!
                LLUUID userID = m_app.CreateUser(firstname, lastname, passwd, regX, regY);
                
                if (userID == LLUUID.Zero) throw new Exception(String.Format("failed to create new user {0} {1}",
                                                                             firstname, lastname));
                
                responseData["success"]     = "true";
                responseData["avatar_uuid"] = userID.ToString();

                response.Value = responseData;

                m_log.InfoFormat("[RADMIN]: User {0} {1} created, UUID {2}", firstname, lastname, userID);
            }
            catch (Exception e) 
            {
                m_log.ErrorFormat("[RADMIN] create user: failed: {0}", e.Message);
                m_log.DebugFormat("[RADMIN] create user: failed: {0}", e.ToString());

                responseData["success"]     = "false";
                responseData["avatar_uuid"] = LLUUID.Zero.ToString();
                responseData["error"]       = e.Message;

                response.Value = responseData;
            }

            return response;
        }

        public XmlRpcResponse XmlRpcLoadXMLMethod(XmlRpcRequest request)
        {
            m_log.Info("[RADMIN]: Received Load XML Administrator Request");
            XmlRpcResponse response = new XmlRpcResponse();
            Hashtable requestData = (Hashtable) request.Params[0];
            Hashtable responseData = new Hashtable();

            try 
            {
                // check completeness
                foreach (string p in new string[] { "password", 
                                                    "region_name", "filename" })
                {
                    if (!requestData.Contains(p)) 
                        throw new Exception(String.Format("missing parameter {0}", p));
                }
                
                // check password
                if (!String.IsNullOrEmpty(requiredPassword) &&
                    (string)requestData["password"] != requiredPassword) throw new Exception("wrong password");
                
                string region_name = (string)requestData["region_name"];
                string filename    = (string)requestData["filename"];
                
                if (!m_app.SceneManager.TrySetCurrentScene(region_name)) 
                    throw new Exception(String.Format("failed to switch to region {0}", region_name));
                m_log.InfoFormat("[RADMIN] Switched to region {0}");

                responseData["switched"] = "true";

                m_app.SceneManager.LoadCurrentSceneFromXml(filename, true, new LLVector3(0, 0, 0));
                responseData["loaded"]   = "true";
                
                response.Value           = responseData;
            }
            catch (Exception e)
            {
                m_log.InfoFormat("[RADMIN] LoadXml: {0}", e.Message);
                m_log.DebugFormat("[RADMIN] LoadXML {0}: {1}", e.ToString());

                responseData["loaded"]  = "false";
                responseData["switched"] = "false";
                responseData["error"]   = e.Message;
                
                response.Value          = responseData;
            }
            
            return response;
        }

        public void Close()
        {
        }
    }
}
