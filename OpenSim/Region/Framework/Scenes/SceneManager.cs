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
 *     * Neither the name of the OpenSimulator Project nor the
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
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using OpenMetaverse;
using log4net;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Physics.Manager;
using Nini.Config;
using OpenSim.Framework.Console;
using OpenSim;
using OpenSim.Server.Base;

namespace OpenSim.Region.Framework.Scenes
{
    /// <summary>
    /// Manager for adding, closing and restarting scenes.
    /// </summary>
    public class SceneManager : IApplicationPlugin
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private List<Scene> m_localScenes;
        private Scene m_currentScene = null;
        private IOpenSimBase m_OpenSimBase;
        private IConfigSource m_config = null;
        private int RegionsFinishedStarting = 0;
        public int AllRegions = 0;
        
        public List<Scene> Scenes
        {
            get { return m_localScenes; }
        }

        public Scene CurrentScene
        {
            get { return m_currentScene; }
        }

        public Scene CurrentOrFirstScene
        {
            get
            {
                if (m_currentScene == null)
                {
                    if (m_localScenes.Count > 0)
                    {
                        return m_localScenes[0];
                    }
                    else
                    {
                        return null;
                    }
                }
                else
                {
                    return m_currentScene;
                }
            }
        }

        public void Initialize(IOpenSimBase openSim)
        {
            m_OpenSimBase = openSim;
            m_localScenes = new List<Scene>();

            m_config = openSim.ConfigSource;

            string StorageDLL = "";

            IConfig dbConfig = m_config.Configs["DatabaseService"];
            IConfig simDataConfig = m_config.Configs["SimulationDataStore"];

            //Default to the database service config
            if (dbConfig != null)
            {
                StorageDLL = dbConfig.GetString("StorageProvider", String.Empty);
            }
            if (simDataConfig != null)
            {
                StorageDLL = simDataConfig.GetString("LocalServiceModule", String.Empty);
            }
            if (StorageDLL == String.Empty)
                StorageDLL = "OpenSim.Data.Null.dll";

            m_simulationDataService = ServerUtils.LoadPlugin<ISimulationDataService>(StorageDLL, new object[] { m_config });

            //Register us!
            m_OpenSimBase.ApplicationRegistry.RegisterInterface<SceneManager>(this);
        }

        public void PostInitialise()
        {
        }

        public string Name
        {
            get { return "SceneManager"; }
        }

        public void Dispose()
        {
        }

        protected ISimulationDataService m_simulationDataService;

        public void Close()
        {
            if (proxyUrl.Length > 0)
            {
                Util.XmlRpcCommand(proxyUrl, "Stop");
            }
            // collect known shared modules in sharedModules
            for (int i = 0; i < m_localScenes.Count; i++)
            {
                // close scene/region
                m_localScenes[i].Close();
            }
            foreach (IClientNetworkServer server in m_clientServers)
            {
                server.Stop();
            }
        }

        public void Add(Scene scene)
        {
            scene.OnRestart += HandleRestart;
            scene.OnStartupComplete += HandleStartupComplete;
            m_localScenes.Add(scene);
        }

        public void HandleRestart(RegionInfo rdata)
        {
            m_log.Error("[SCENEMANAGER]: Got Restart message for region : " + rdata.RegionName + ".");
            RegionInfo info = null;
            int RegionSceneElement = -1;
            for (int i = 0; i < m_localScenes.Count; i++)
            {
                if (rdata.RegionName == m_localScenes[i].RegionInfo.RegionName)
                {
                    RegionSceneElement = i;
                }
            }

            // Now we make sure the region is no longer known about by the SceneManager
            // Prevents duplicates.
            info = m_localScenes[RegionSceneElement].RegionInfo;
            if (RegionSceneElement >= 0)
            {
                m_localScenes.RemoveAt(RegionSceneElement);
            }

            
            ShutdownClientServer(info);
            IScene scene;
            CreateRegion(info, true, out scene);
        }

        public void HandleStartupComplete(IScene scene, List<string> data)
        {
            RegionsFinishedStarting++;
            if (RegionsFinishedStarting == AllRegions)
            {
                FinishStartUp();
            }
        }

        private void FinishStartUp()
        {
            PrintFileToConsole("startuplogo.txt");

            // For now, start at the 'root' level by default
            if (Scenes.Count == 1)
            {
                // If there is only one region, select it
                ChangeSelectedRegion(Scenes[0].RegionInfo.RegionName);
            }
            else
            {
                ChangeSelectedRegion("root");
            }

            TimeSpan timeTaken = DateTime.Now - m_OpenSimBase.StartupTime;

            m_log.InfoFormat("[SCENEMANAGER]: Startup is complete and took {0}m {1}s", timeTaken.Minutes, timeTaken.Seconds);
        }

        public void ChangeSelectedRegion(string newRegionName)
        {
            if (!TrySetCurrentScene(newRegionName))
            {
                MainConsole.Instance.Output(String.Format("Couldn't select region {0}", newRegionName));
                return;
            }
            
            string regionName = (CurrentScene == null ? "root" : CurrentScene.RegionInfo.RegionName);
            MainConsole.Instance.DefaultPrompt = String.Format("Region ({0}) ", regionName);
            MainConsole.Instance.ConsoleScene = CurrentScene;
        }

        /// <summary>
        /// Opens a file and uses it as input to the console command parser.
        /// </summary>
        /// <param name="fileName">name of file to use as input to the console</param>
        private void PrintFileToConsole(string fileName)
        {
            if (File.Exists(fileName))
            {
                StreamReader readFile = File.OpenText(fileName);
                string currentLine;
                while ((currentLine = readFile.ReadLine()) != null)
                {
                    m_log.Info("[!]" + currentLine);
                }
            }
        }

        /// <summary>
        /// Load an xml file of prims in OpenSimulator's current 'xml2' file format to the current scene
        /// </summary>
        public void LoadCurrentSceneFromXml2(string filename)
        {
            IRegionSerialiserModule serialiser = CurrentOrFirstScene.RequestModuleInterface<IRegionSerialiserModule>();
            if (serialiser != null)
                serialiser.LoadPrimsFromXml2(CurrentOrFirstScene, filename);
        }

        /// <summary>
        /// Save the current scene to an OpenSimulator archive.  This archive will eventually include the prim's assets
        /// as well as the details of the prims themselves.
        /// </summary>
        /// <param name="cmdparams"></param>
        public void SaveCurrentSceneToArchive(string[] cmdparams)
        {
            IRegionArchiverModule archiver = CurrentOrFirstScene.RequestModuleInterface<IRegionArchiverModule>();
            if (archiver != null)
                archiver.HandleSaveOarConsoleCommand(string.Empty, cmdparams);
        }

        /// <summary>
        /// Load an OpenSim archive into the current scene.  This will load both the shapes of the prims and upload
        /// their assets to the asset service.
        /// </summary>
        /// <param name="cmdparams"></param>
        public void LoadArchiveToCurrentScene(string[] cmdparams)
        {
            IRegionArchiverModule archiver = CurrentOrFirstScene.RequestModuleInterface<IRegionArchiverModule>();
            if (archiver != null)
                archiver.HandleLoadOarConsoleCommand(string.Empty, cmdparams);
        }

        public void SendCommandToPluginModules(string[] cmdparams)
        {
            ForEachCurrentScene(delegate(Scene scene) { scene.SendCommandToPlugins(cmdparams); });
        }

        public void SetBypassPermissionsOnCurrentScene(bool bypassPermissions)
        {
            ForEachCurrentScene(delegate(Scene scene) { scene.Permissions.SetBypassPermissions(bypassPermissions); });
        }

        private void ForEachCurrentScene(Action<Scene> func)
        {
            if (m_currentScene == null)
            {
                m_localScenes.ForEach(func);
            }
            else
            {
                func(m_currentScene);
            }
        }

        public void RestartCurrentScene()
        {
            ForEachCurrentScene(delegate(Scene scene) { scene.RestartNow(); });
        }

        public void BackupCurrentScene()
        {
            ForEachCurrentScene(delegate(Scene scene)
            {
                scene.ProcessPrimBackupTaints(true); 
            });
        }

        public bool TrySetCurrentScene(string regionName)
        {
            if ((String.Compare(regionName, "root") == 0) 
                || (String.Compare(regionName, "..") == 0)
                || (String.Compare(regionName, "/") == 0))
            {
                m_currentScene = null;
                return true;
            }
            else
            {
                foreach (Scene scene in m_localScenes)
                {
                    if (String.Compare(scene.RegionInfo.RegionName, regionName, true) == 0)
                    {
                        m_currentScene = scene;
                        return true;
                    }
                }

                return false;
            }
        }

        public bool TrySetCurrentScene(UUID regionID)
        {
            m_log.Debug("Searching for Region: '" + regionID + "'");

            foreach (Scene scene in m_localScenes)
            {
                if (scene.RegionInfo.RegionID == regionID)
                {
                    m_currentScene = scene;
                    return true;
                }
            }

            return false;
        }

        #region TryGetScene functions

        public bool TryGetScene(string regionName, out Scene scene)
        {
            foreach (Scene mscene in m_localScenes)
            {
                if (String.Compare(mscene.RegionInfo.RegionName, regionName, true) == 0)
                {
                    scene = mscene;
                    return true;
                }
            }
            scene = null;
            return false;
        }

        public bool TryGetScene(UUID regionID, out Scene scene)
        {
            foreach (Scene mscene in m_localScenes)
            {
                if (mscene.RegionInfo.RegionID == regionID)
                {
                    scene = mscene;
                    return true;
                }
            }
            
            scene = null;
            return false;
        }

        public bool TryGetScene(uint locX, uint locY, out Scene scene)
        {
            foreach (Scene mscene in m_localScenes)
            {
                if (mscene.RegionInfo.RegionLocX == locX &&
                    mscene.RegionInfo.RegionLocY == locY)
                {
                    scene = mscene;
                    return true;
                }
            }
            
            scene = null;
            return false;
        }

        public bool TryGetScene(IPEndPoint ipEndPoint, out Scene scene)
        {
            foreach (Scene mscene in m_localScenes)
            {
                if ((mscene.RegionInfo.InternalEndPoint.Equals(ipEndPoint.Address)) &&
                    (mscene.RegionInfo.InternalEndPoint.Port == ipEndPoint.Port))
                {
                    scene = mscene;
                    return true;
                }
            }
            
            scene = null;
            return false;
        }

        #endregion

        /// <summary>
        /// Set the debug packet level on the current scene.  This level governs which packets are printed out to the
        /// console.
        /// </summary>
        /// <param name="newDebug"></param>
        public void SetDebugPacketLevelOnCurrentScene(int newDebug)
        {
            ForEachCurrentScene(
                delegate(Scene scene)
                {
                    scene.ForEachScenePresence(delegate(ScenePresence scenePresence)
                    {
                        if (!scenePresence.IsChildAgent)
                        {
                            m_log.DebugFormat("Packet debug for {0} {1} set to {2}",
                                              scenePresence.Firstname,
                                              scenePresence.Lastname,
                                              newDebug);

                            scenePresence.ControllingClient.SetDebugPacketLevel(newDebug);
                        }
                    });
                }
            );
        }

        public List<ScenePresence> GetCurrentSceneAvatars()
        {
            List<ScenePresence> avatars = new List<ScenePresence>();

            ForEachCurrentScene(
                delegate(Scene scene)
                {
                    scene.ForEachScenePresence(delegate(ScenePresence scenePresence)
                    {
                        if (!scenePresence.IsChildAgent)
                            avatars.Add(scenePresence);
                    });
                }
            );

            return avatars;
        }

        public List<ScenePresence> GetCurrentScenePresences()
        {
            List<ScenePresence> presences = new List<ScenePresence>();

            ForEachCurrentScene(delegate(Scene scene)
            {
                scene.ForEachScenePresence(delegate(ScenePresence sp)
                {
                    presences.Add(sp);
                });
            });

            return presences;
        }

        public void ForceCurrentSceneClientUpdate()
        {
            ForEachCurrentScene(delegate(Scene scene) { scene.ForceClientUpdate(); });
        }

        public bool TryGetScenePresence(UUID avatarId, out ScenePresence avatar)
        {
            foreach (Scene scene in m_localScenes)
            {
                if (scene.TryGetScenePresence(avatarId, out avatar))
                {
                    return true;
                }
            }

            avatar = null;
            return false;
        }

        public bool TryGetAvatarsScene(UUID avatarId, out Scene scene)
        {
            ScenePresence avatar = null;
            foreach (Scene mScene in m_localScenes)
            {
                if (mScene.TryGetScenePresence(avatarId, out avatar))
                {
                    scene = mScene;
                    return true;
                }
            }

            scene = null;
            return false;
        }

        public void CloseScene(Scene scene)
        {
            m_localScenes.Remove(scene);
            scene.Close();
        }

        public bool TryGetAvatarByName(string avatarName, out ScenePresence avatar)
        {
            foreach (Scene scene in m_localScenes)
            {
                if (scene.TryGetAvatarByName(avatarName, out avatar))
                {
                    return true;
                }
            }

            avatar = null;
            return false;
        }

        public void ForEachScene(Action<Scene> action)
        {
            m_localScenes.ForEach(action);
        }

        private string proxyUrl = "";
        private int proxyOffset = 0;
        private string SecretID = UUID.Random().ToString();

        /// <summary>
        /// Execute the region creation process.  This includes setting up scene infrastructure.
        /// </summary>
        /// <param name="regionInfo"></param>
        /// <param name="portadd_flag"></param>
        /// <param name="do_post_init"></param>
        /// <returns></returns>
        public IClientNetworkServer CreateRegion(RegionInfo regionInfo, bool portadd_flag, out IScene mscene)
        {
            int port = regionInfo.InternalEndPoint.Port;

            // set initial ServerURI
            regionInfo.ServerURI = "http://" + regionInfo.ExternalHostName + ":" + regionInfo.InternalEndPoint.Port;
            regionInfo.HttpPort = MainServer.Instance.Port;

            regionInfo.osSecret = SecretID;

            IConfig networkConfig = m_config.Configs["Network"];
            if (networkConfig != null)
            {
                proxyUrl = networkConfig.GetString("proxy_url", "");
                proxyOffset = Int32.Parse(networkConfig.GetString("proxy_offset", "0"));
            }

            if ((proxyUrl.Length > 0) && (portadd_flag))
            {
                // set proxy url to RegionInfo
                regionInfo.proxyUrl = proxyUrl;
                regionInfo.ProxyOffset = proxyOffset;
                Util.XmlRpcCommand(proxyUrl, "AddPort", port, port + proxyOffset, regionInfo.ExternalHostName);
            }

            IClientNetworkServer clientServer = null;
            Scene scene = SetupScene(regionInfo, proxyOffset, m_config, out clientServer);

            m_log.Info("[MODULES]: Loading New Style Region's modules");
            IRegionModulesController controller;
            if (m_OpenSimBase.ApplicationRegistry.TryGet(out controller))
            {
                controller.AddRegionToModules(scene);
            }
            else
                m_log.Error("[MODULES]: The new RegionModulesController is missing...");

            scene.SetModuleInterfaces();

            // Prims have to be loaded after module configuration since some modules may be invoked during the load
            scene.LoadPrimsFromStorage(regionInfo.RegionID);

            scene.loadAllLandObjectsFromStorage(regionInfo.RegionID);
            scene.EventManager.TriggerParcelPrimCountUpdate();

            RegisterRegionWithGrid(scene);

            // We need to do this after we've initialized the
            // scripting engines.
            scene.CreateScriptInstances();

            clientServer.Start();
            scene.EventManager.OnShutdown += delegate() { ShutdownRegion(scene); };

            mscene = scene;

            scene.StartTimer();
            //Tell the scene that the startup is complete 
            // Note: this event is added in the scene constructor
            scene.FinishedStartup("Startup", new List<string>());

            return clientServer;
        }

        private void RegisterRegionWithGrid(Scene scene)
        {
            string error = scene.RegisterRegionWithGrid();
            if (error != "")
            {
                if (error == "Region location is reserved")
                {
                    m_log.Error("[STARTUP]: Registration of region with grid failed - The region location you specified is reserved. You must move your region.");
                    scene.RegionInfo.RegionLocY = uint.Parse(MainConsole.Instance.CmdPrompt("New Region Location X", "1000"));
                    scene.RegionInfo.RegionLocX = uint.Parse(MainConsole.Instance.CmdPrompt("New Region Location Y", "1000"));

                    Aurora.DataManager.DataManager.RequestPlugin<Aurora.Framework.IRegionInfoConnector>().UpdateRegionInfo(scene.RegionInfo, false);
                }
                if (error == "Region overlaps another region")
                {
                    m_log.Error("[STARTUP]: Registration of region with grid failed - The region location you specified is already in use. You must move your region.");
                    scene.RegionInfo.RegionLocY = uint.Parse(MainConsole.Instance.CmdPrompt("New Region Location X", "1000"));
                    scene.RegionInfo.RegionLocX = uint.Parse(MainConsole.Instance.CmdPrompt("New Region Location Y", "1000"));
                    
                    IConfig config = m_config.Configs["RegionStartup"];
                    if (config != null)
                    {
                        //TERRIBLE! Needs to be modular, but we can't access the module from a scene module!
                        if (config.GetString("Default") == "RegionLoaderDataBaseSystem")
                            Aurora.DataManager.DataManager.RequestPlugin<Aurora.Framework.IRegionInfoConnector>().UpdateRegionInfo(scene.RegionInfo, false);
                        else
                            SaveChangesFile(scene.RegionInfo);
                    }
                    else
                        SaveChangesFile(scene.RegionInfo);
                }
                if (error.Contains("Can't move this region"))
                {
                    m_log.Error("[STARTUP]: Registration of region with grid failed - You can not move this region. Moving it back to its original position.");
                    //Opensim Grid Servers don't have this functionality.
                    try
                    {
                        string[] position = error.Split(',');

                        scene.RegionInfo.RegionLocY = uint.Parse(position[1]);
                        scene.RegionInfo.RegionLocX = uint.Parse(position[2]);

                        IConfig config = m_config.Configs["RegionStartup"];
                        if (config != null)
                        {
                            //TERRIBLE! Needs to be modular, but we can't access the module from a scene module!
                            if (config.GetString("Default") == "RegionLoaderDataBaseSystem")
                                Aurora.DataManager.DataManager.RequestPlugin<Aurora.Framework.IRegionInfoConnector>().UpdateRegionInfo(scene.RegionInfo, false);
                            else
                                SaveChangesFile(scene.RegionInfo);
                        }
                        else
                            SaveChangesFile(scene.RegionInfo);
                    }
                    catch (Exception e)
                    {
                        m_log.Error("Unable to move the region back to its original position, is this an opensim server? Please manually move the region back.");
                        throw e;
                    }
                }
                if (error == "Duplicate region name")
                {
                    m_log.Error("[STARTUP]: Registration of region with grid failed - The region name you specified is already in use. Please change the name.");
                    scene.RegionInfo.RegionName = MainConsole.Instance.CmdPrompt("New Region Name", "");

                    IConfig config = m_config.Configs["RegionStartup"];
                    if (config != null)
                    {
                        //TERRIBLE! Needs to be modular, but we can't access the module from a scene module!
                        if (config.GetString("Default") == "RegionLoaderDataBaseSystem")
                            Aurora.DataManager.DataManager.RequestPlugin<Aurora.Framework.IRegionInfoConnector>().UpdateRegionInfo(scene.RegionInfo, false);
                        else
                            SaveChangesFile(scene.RegionInfo);
                    }
                    else
                        SaveChangesFile(scene.RegionInfo);
                }
                if (error == "Region locked out")
                {
                    m_log.Error("[STARTUP]: Registration of region with grid failed - The region you are attempting to join has been blocked from connecting. Please connect another region.");
                    throw new Exception(error);
                }
                if (error == "Error communicating with grid service")
                {
                    m_log.Error("[STARTUP]: Registration of region with grid failed - The grid service can not be found! Please make sure that you can connect to the grid server and that the grid server is on.");
                    string input = MainConsole.Instance.CmdPrompt("Press enter when you are ready to proceed, or type cancel to exit");
                    if (input == "cancel")
                    {
                        Environment.Exit(0);
                    }
                }
                if (error == "Wrong Session ID")
                {
                    m_log.Error("[STARTUP]: Registration of region with grid failed - Wrong Session ID for this region!");
                    string input = MainConsole.Instance.CmdPrompt("Press enter when you are ready to proceed, or type cancel to exit");
                    if (input == "cancel")
                    {
                        Environment.Exit(0);
                    }
                }
                RegisterRegionWithGrid(scene);
            }
        }

        private void SaveChangesFile(RegionInfo regionInfo)
        {
            string regionConfigPath = Path.Combine(Util.configDir(), "Regions");

            try
            {
                IConfig config = m_config.Configs["RegionStartup"];
                if (config != null)
                {
                    regionConfigPath = config.GetString("RegionsDirectory", regionConfigPath).Trim();
                }
            }
            catch (Exception)
            {
                // No INI setting recorded.
            }
            if (!Directory.Exists(regionConfigPath))
                return;

            string[] iniFiles = Directory.GetFiles(regionConfigPath, "*.ini");
            foreach (string file in iniFiles)
            {
                IConfigSource source = new IniConfigSource(file, Nini.Ini.IniFileType.AuroraStyle);
                IConfig cnf = source.Configs[regionInfo.RegionName];
                if (cnf != null)
                {
                    cnf.Set("Location", regionInfo.RegionLocX + "," + regionInfo.RegionLocY);
                    cnf.Set("RegionType", regionInfo.RegionType);
                    cnf.Name = regionInfo.RegionName;
                    source.Save();
                    break;
                }
            }
        }

        private void ShutdownRegion(Scene scene)
        {
            m_log.DebugFormat("[SHUTDOWN]: Shutting down region {0}", scene.RegionInfo.RegionName);
            IRegionModulesController controller;
            if (m_OpenSimBase.ApplicationRegistry.TryGet<IRegionModulesController>(out controller))
            {
                controller.RemoveRegionFromModules(scene);
            }
        }

        protected List<IClientNetworkServer> m_clientServers = new List<IClientNetworkServer>();
        public List<IClientNetworkServer> ClientServers
        {
            get { return m_clientServers; }
        }

        public void ShutdownClientServer(RegionInfo whichRegion)
        {
            // Close and remove the clientserver for a region
            bool foundClientServer = false;
            int clientServerElement = 0;
            Location location = new Location(whichRegion.RegionHandle);

            for (int i = 0; i < m_clientServers.Count; i++)
            {
                if (m_clientServers[i].HandlesRegion(location))
                {
                    clientServerElement = i;
                    foundClientServer = true;
                    break;
                }
            }

            if (foundClientServer)
            {
                m_clientServers[clientServerElement].NetworkStop();
                m_clientServers.RemoveAt(clientServerElement);
            }
        }

        public void RemoveRegion(IScene scene, bool cleanup)
        {
            // only need to check this if we are not at the
            // root level
            if ((CurrentScene != null) && (CurrentScene.RegionInfo.RegionID == scene.RegionInfo.RegionID))
            {
                TrySetCurrentScene("..");
            }

            ((Scene)scene).DeleteAllSceneObjects();
            CloseScene((Scene)scene);
            ShutdownClientServer(scene.RegionInfo);

            if (!cleanup)
                return;

            if (!String.IsNullOrEmpty(scene.RegionInfo.RegionFile))
            {
                if (scene.RegionInfo.RegionFile.ToLower().EndsWith(".xml"))
                {
                    File.Delete(scene.RegionInfo.RegionFile);
                    m_log.InfoFormat("[OPENSIM]: deleting region file \"{0}\"", scene.RegionInfo.RegionFile);
                }
                if (scene.RegionInfo.RegionFile.ToLower().EndsWith(".ini"))
                {
                    try
                    {
                        IniConfigSource source = new IniConfigSource(scene.RegionInfo.RegionFile, Nini.Ini.IniFileType.AuroraStyle);
                        if (source.Configs[scene.RegionInfo.RegionName] != null)
                        {
                            source.Configs.Remove(scene.RegionInfo.RegionName);

                            if (source.Configs.Count == 0)
                            {
                                File.Delete(scene.RegionInfo.RegionFile);
                            }
                            else
                            {
                                source.Save(scene.RegionInfo.RegionFile);
                            }
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }

        public void RemoveRegion(string name, bool cleanUp)
        {
            Scene target;
            if (TryGetScene(name, out target))
                RemoveRegion(target, cleanUp);
        }

        /// <summary>
        /// Remove a region from the simulator without deleting it permanently.
        /// </summary>
        /// <param name="scene"></param>
        /// <returns></returns>
        public void CloseRegion(IScene scene)
        {
            // only need to check this if we are not at the
            // root level
            if ((CurrentScene != null) && (CurrentScene.RegionInfo.RegionID == scene.RegionInfo.RegionID))
            {
                TrySetCurrentScene("..");
            }

            CloseScene((Scene)scene);
            ShutdownClientServer(scene.RegionInfo);
        }

        /// <summary>
        /// Create a scene and its initial base structures.
        /// </summary>
        /// <param name="regionInfo"></param>
        /// <param name="proxyOffset"></param>
        /// <param name="configSource"></param>
        /// <param name="clientServer"> </param>
        /// <returns></returns>
        protected Scene SetupScene(RegionInfo regionInfo, int proxyOffset, IConfigSource configSource, out IClientNetworkServer clientServer)
        {
            AgentCircuitManager circuitManager = new AgentCircuitManager();
            IPAddress listenIP = regionInfo.InternalEndPoint.Address;
            //if (!IPAddress.TryParse(regionInfo.InternalEndPoint, out listenIP))
            //    listenIP = IPAddress.Parse("0.0.0.0");

            uint port = (uint)regionInfo.InternalEndPoint.Port;

            string ClientstackDll = m_config.Configs["Startup"].GetString("ClientStackPlugin", "OpenSim.Region.ClientStack.LindenUDP.dll");

            clientServer = Aurora.Framework.AuroraModuleLoader.LoadPlugin<IClientNetworkServer>(ClientstackDll, "IClientNetworkServer");
            clientServer.Initialise(
                    listenIP, ref port, proxyOffset, regionInfo.m_allow_alternate_ports,
                    m_config, circuitManager);
            
            regionInfo.InternalEndPoint.Port = (int)port;

            SceneCommunicationService sceneGridService = new SceneCommunicationService();
            Scene scene = new Scene(regionInfo, circuitManager, sceneGridService, m_config, m_OpenSimBase.Version, m_simulationDataService, m_OpenSimBase.Stats);
            
            clientServer.AddScene(scene);
            m_clientServers.Add(clientServer);

            //Do this here so that we don't have issues later when startup complete messages start coming in
            Add(scene);

            scene.PhysicsScene = GetPhysicsScene(m_config, scene.RegionInfo.RegionName);
            scene.PhysicsScene.SetTerrain(scene.Heightmap.GetFloatsSerialised(), scene.Heightmap.GetDoubles());
            scene.PhysicsScene.SetWaterLevel((float)regionInfo.RegionSettings.WaterHeight);

            return scene;
        }

        /// <summary>
        /// Get a new physics scene.
        /// </summary>
        /// <param name="config">The configuration data to pass to the physics and mesh engines</param>
        /// <param name="osSceneIdentifier">
        /// The name of the OpenSim scene this physics scene is serving.  This will be used in log messages.
        /// </param>
        /// <returns></returns>
        protected PhysicsScene GetPhysicsScene(IConfigSource config, string RegionName)
        {
            IConfig PhysConfig = config.Configs["Physics"];
            IConfig MeshingConfig = config.Configs["Meshing"];
            string engine = "";
            string meshEngine = "";
            if (PhysConfig != null)
            {
                engine = PhysConfig.GetString("DefaultPhysicsEngine", "OpenDynamicsEngine");
                meshEngine = MeshingConfig.GetString("DefaultMeshingEngine", "Meshmerizer");
                string regionName = RegionName.Trim().Replace(' ', '_');
                string RegionPhysicsEngine = PhysConfig.GetString("Region_" + regionName + "_PhysicsEngine", String.Empty);
                if (RegionPhysicsEngine != "")
                    engine = RegionPhysicsEngine;
                string RegionMeshingEngine = MeshingConfig.GetString("Region_" + regionName + "_MeshingEngine", String.Empty);
                if (RegionMeshingEngine != "")
                    meshEngine = RegionMeshingEngine;
            }
            else
            {
                //Load Sane defaults
                engine = "OpenDynamicsEngine";
                meshEngine = "Meshmerizer";
            }
            PhysicsPluginManager physicsPluginManager = new PhysicsPluginManager();
            physicsPluginManager.LoadPluginsFromAssemblies("Physics");

            return physicsPluginManager.GetPhysicsScene(engine, meshEngine, config, RegionName);
        }
    }
}
