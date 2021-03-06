﻿using System;
using System.Collections.Generic;
using System.Text;
using log4net;
using System.Reflection;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Region.Framework.Interfaces;
using Nini.Config;

namespace Aurora.Modules
{
    public class ObjectCacheModule : ISharedRegionModule, IObjectCache
    {
        #region Declares

        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        protected bool m_Enabled = true;
        private Dictionary<UUID, ObjectCacheClient> ObjectCacheAgents = new Dictionary<UUID, ObjectCacheClient>();

        #endregion

        #region ISharedRegionModule

        public virtual void Initialise(IConfigSource source)
        {
            IConfig moduleConfig = source.Configs["ObjectCache"];
            if (moduleConfig != null)
                m_Enabled = moduleConfig.GetString("Module", "") == Name;
        }

        public virtual void PostInitialise()
        {
        }

        public virtual void AddRegion(Scene scene)
        {
            if (!m_Enabled)
                return;

            scene.RegisterModuleInterface<IObjectCache>(this);
        }

        public virtual void RemoveRegion(Scene scene)
        {
            if (!m_Enabled)
                return;

            scene.UnregisterModuleInterface<IObjectCache>(this);
        }

        public virtual void RegionLoaded(Scene scene)
        {
        }

        public virtual void Close()
        {
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public virtual string Name
        {
            get { return "ObjectCacheModule"; }
        }

        #endregion

        #region IObjectCache

        public bool UseCachedObject(UUID AgentID, uint localID, uint CurrentEntityCRC)
        {
            ObjectCacheClient client;
            if (ObjectCacheAgents.TryGetValue(AgentID, out client))
                return client.GetUseCachedObject(localID, CurrentEntityCRC);
            else
            {
                client = new ObjectCacheClient();
                ObjectCacheAgents[AgentID] = client;
                return client.GetUseCachedObject(localID, CurrentEntityCRC);
            }
        }

        private class ObjectCacheClient
        {
            //Should we use a dictionary for ObjectUpdateCached checking?
            Dictionary<uint, uint> m_cachedObjects = new Dictionary<uint, uint>();

            public bool GetUseCachedObject(uint localID, uint CurrentEntityCRC)
            {
                uint CRC = 0;
                if (!m_cachedObjects.TryGetValue(localID, out CRC))
                {
                    //Add to the cache
                    m_cachedObjects[localID] = CurrentEntityCRC;
                    return false;
                }
                else
                {
                    if (CurrentEntityCRC > CRC)
                    {
                        m_cachedObjects[localID] = CurrentEntityCRC;
                        return false; //CRC is greater than the one the client cache has
                    }
                    else
                        return true; //CRC is the same! Send a cache!
                }
            }
        }

        #endregion
    }
}
