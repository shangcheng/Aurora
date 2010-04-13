﻿/*
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
using System.Reflection;
using log4net;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Framework.Servers;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Framework.Client;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using Aurora.Framework;

namespace Aurora.Modules
{
    public class AuroraOfflineMessageModule : ISharedRegionModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private bool enabled = true;
        private List<Scene> m_SceneList = new List<Scene>();
        private string m_RestURL = String.Empty;
        IMessageTransferModule m_TransferModule = null;
        private bool m_ForwardOfflineGroupMessages = true;
        private IRegionData RegionData;

        public void Initialise(IConfigSource config)
        {
            IConfig cnf = config.Configs["Messaging"];
            if (cnf == null)
            {
                enabled = false;
                return;
            }
            if (cnf != null && cnf.GetString("OfflineMessageModule", "AuroraOfflineMessageModule") !=
                    "AuroraOfflineMessageModule")
            {
                enabled = false;
                return;
            }

            m_ForwardOfflineGroupMessages = cnf.GetBoolean("ForwardOfflineGroupMessages", m_ForwardOfflineGroupMessages);
        }

        public void AddRegion(Scene scene)
        {
            if (!enabled)
                return;

            lock (m_SceneList)
            {
                m_SceneList.Add(scene);

                scene.EventManager.OnNewClient += OnNewClient;
            }
        }

        public void RegionLoaded(Scene scene)
        {
            if (!enabled)
                return;

            if (m_TransferModule == null)
            {
                m_TransferModule = scene.RequestModuleInterface<IMessageTransferModule>();
                if (m_TransferModule == null)
                {
                    scene.EventManager.OnNewClient -= OnNewClient;

                    enabled = false;
                    m_SceneList.Clear();

                    m_log.Error("[OFFLINE MESSAGING] No message transfer module is enabled. Diabling offline messages");
                }
                m_TransferModule.OnUndeliveredMessage += UndeliveredMessage;
            }
        }

        public void RemoveRegion(Scene scene)
        {
            if (!enabled)
                return;

            lock (m_SceneList)
            {
                m_SceneList.Remove(scene);
            }
        }

        public void PostInitialise()
        {
            if (!enabled)
                return;

            m_log.Debug("[OFFLINE MESSAGING] Offline messages enabled");
            RegionData = Aurora.DataManager.DataManager.GetRegionPlugin();
        }

        public string Name
        {
            get { return "AuroraOfflineMessageModule"; }
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public void Close()
        {
        }

        private Scene FindScene(UUID agentID)
        {
            foreach (Scene s in m_SceneList)
            {
                ScenePresence presence = s.GetScenePresence(agentID);
                if (presence != null && !presence.IsChildAgent)
                    return s;
            }
            return null;
        }

        private IClientAPI FindClient(UUID agentID)
        {
            foreach (Scene s in m_SceneList)
            {
                ScenePresence presence = s.GetScenePresence(agentID);
                if (presence != null && !presence.IsChildAgent)
                    return presence.ControllingClient;
            }
            return null;
        }

        private void OnNewClient(IClientAPI client)
        {
            client.OnRetrieveInstantMessages += RetrieveInstantMessages;
        }

        private void RetrieveInstantMessages(IClientAPI client)
        {
            m_log.DebugFormat("[OFFLINE MESSAGING] Retrieving stored messages for {0}", client.AgentId);

            OfflineMessage[] msglist = RegionData.GetOfflineMessages(client.AgentId.ToString());

            foreach (OfflineMessage IMtext in msglist)
            {
                GridInstantMessage IM = new GridInstantMessage(
                                    null, new UUID(IMtext.FromUUID), IMtext.FromName,
                                    new UUID(IMtext.ToUUID),
                                    (byte)InstantMessageDialog.BusyAutoResponse,
                                    IMtext.Message, true,
                                    new Vector3());

                // client.SendInstantMessage(im);

                // Send through scene event manager so all modules get a chance
                // to look at this message before it gets delivered.
                //
                // Needed for proper state management for stored group
                // invitations
                //
                Scene s = FindScene(client.AgentId);
                if (s != null)
                    s.EventManager.TriggerIncomingInstantMessage(IM);
            }
        }

        private void UndeliveredMessage(GridInstantMessage im)
        {
            if ((im.offline != 0)
                && (!im.fromGroup || (im.fromGroup && m_ForwardOfflineGroupMessages)))
            {
                RegionData.AddOfflineMessage(im.fromAgentID.ToString(), im.fromAgentName, im.toAgentID.ToString(), im.message);

                if (im.dialog == (byte)InstantMessageDialog.MessageFromAgent)
                {
                    IClientAPI client = FindClient(new UUID(im.fromAgentID));
                    if (client == null)
                        return;

                    client.SendInstantMessage(new GridInstantMessage(
                            null, new UUID(im.toAgentID),
                            "System", new UUID(im.fromAgentID),
                            (byte)InstantMessageDialog.MessageFromAgent,
                            "User is not logged in. Message saved.",
                            false, new Vector3()));
                }
            }
        }
    }
}
