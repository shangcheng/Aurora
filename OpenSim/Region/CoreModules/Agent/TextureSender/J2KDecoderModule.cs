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
using System.Reflection;
using System.Text;
using System.Threading;
using log4net;
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.Imaging;
using CSJ2K;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Services.Interfaces;

namespace OpenSim.Region.CoreModules.Agent.TextureSender
{
    public delegate void J2KDecodeDelegate(UUID assetID);

    public class J2KDecoderModule : ISharedRegionModule, IJ2KDecoder
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>Temporarily holds deserialized layer data information in memory</summary>
        private readonly ExpiringCache<UUID, OpenJPEG.J2KLayerInfo[]> m_decodedCache = new ExpiringCache<UUID,OpenJPEG.J2KLayerInfo[]>();
        /// <summary>List of client methods to notify of results of decode</summary>
        private readonly Dictionary<UUID, List<DecodedCallback>> m_notifyList = new Dictionary<UUID, List<DecodedCallback>>();
        /// <summary>Cache that will store decoded JPEG2000 layer boundary data</summary>
        private IImprovedAssetCache m_cache;
        /// <summary>Reference to a scene (doesn't matter which one as long as it can load the cache module)</summary>
        private Scene m_scene;
        private bool m_useCache = true;

        #region IRegionModule

        private bool m_useCSJ2K = true;

        public string Name { get { return "J2KDecoderModule"; } }
        public bool IsSharedModule { get { return true; } }

        public J2KDecoderModule()
        {
        }

        public void Initialise(IConfigSource source)
        {
            IConfig imageConfig = source.Configs["ImageDecoding"];
            if (imageConfig != null)
            {
                m_useCSJ2K = imageConfig.GetBoolean("UseCSJ2K", m_useCSJ2K);
                m_useCache = imageConfig.GetBoolean("UseJ2KCache", m_useCache);
            }
        }

        public void AddRegion(Scene scene)
        {
            if (m_scene == null)
                m_scene = scene;

            scene.RegisterModuleInterface<IJ2KDecoder>(this);
        }

        public void RemoveRegion(Scene scene)
        {

        }

        public void RegionLoaded(Scene scene)
        {
            m_cache = m_scene.RequestModuleInterface<IImprovedAssetCache>();
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public void PostInitialise()
        {
        }

        public void Close()
        {
        }

        #endregion IRegionModule

        #region IJ2KDecoder

        public void BeginDecode(UUID assetID, byte[] j2kData, DecodedCallback callback)
        {
            OpenJPEG.J2KLayerInfo[] result;

            // If it's cached, return the cached results
            if (m_decodedCache.TryGetValue(assetID, out result))
            {
                callback(assetID, result);
            }
            else
            {
                // Not cached, we need to decode it.
                // Add to notify list and start decoding.
                // Next request for this asset while it's decoding will only be added to the notify list
                // once this is decoded, requests will be served from the cache and all clients in the notifylist will be updated
                bool decode = false;
                lock (m_notifyList)
                {
                    if (m_notifyList.ContainsKey(assetID))
                    {
                        m_notifyList[assetID].Add(callback);
                    }
                    else
                    {
                        List<DecodedCallback> notifylist = new List<DecodedCallback>();
                        notifylist.Add(callback);
                        m_notifyList.Add(assetID, notifylist);
                        decode = true;
                    }
                }

                // Do Decode!
                if (decode)
                    DoJ2KDecode(assetID, j2kData);
            }
        }

        /// <summary>
        /// Provides a synchronous decode so that caller can be assured that this executes before the next line
        /// </summary>
        /// <param name="assetID"></param>
        /// <param name="j2kData"></param>
        public bool Decode(UUID assetID, byte[] j2kData)
        {
            return DoJ2KDecode(assetID, j2kData);
        }

        #endregion IJ2KDecoder

        /// <summary>
        /// Decode Jpeg2000 Asset Data
        /// </summary>
        /// <param name="assetID">UUID of Asset</param>
        /// <param name="j2kData">JPEG2000 data</param>
        private bool DoJ2KDecode(UUID assetID, byte[] j2kData)
        {
            //int DecodeTime = 0;
            //DecodeTime = Environment.TickCount;
            OpenJPEG.J2KLayerInfo[] layers;

            if (!TryLoadCacheForAsset(assetID, out layers))
            {
                if (m_useCSJ2K)
                {
                    try
                    {
                        List<int> layerStarts = CSJ2K.J2kImage.GetLayerBoundaries(new MemoryStream(j2kData));

                        if (layerStarts != null && layerStarts.Count > 0)
                        {
                            layers = new OpenJPEG.J2KLayerInfo[layerStarts.Count];

                            for (int i = 0; i < layerStarts.Count; i++)
                            {
                                OpenJPEG.J2KLayerInfo layer = new OpenJPEG.J2KLayerInfo();

                                if (i == 0)
                                    layer.Start = 0;
                                else
                                    layer.Start = layerStarts[i];

                                if (i == layerStarts.Count - 1)
                                    layer.End = j2kData.Length;
                                else
                                    layer.End = layerStarts[i + 1] - 1;

                                layers[i] = layer;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        m_log.Warn("[J2KDecoderModule]: CSJ2K threw an exception decoding texture " + assetID + ": " + ex.Message);
                    }
                }
                else
                {
                    int components;
                    if (!OpenJPEG.DecodeLayerBoundaries(j2kData, out layers, out components))
                    {
                        m_log.Warn("[J2KDecoderModule]: OpenJPEG failed to decode texture " + assetID);
                    }
                }

                if (layers == null || layers.Length == 0)
                {
                    m_log.Warn("[J2KDecoderModule]: Failed to decode layer data (" + (m_useCSJ2K ? "CSJ2K" : "OpenJPEG") + ") for texture " + assetID + ", length " + j2kData.Length + " guessing sane defaults");
                    // Layer decoding completely failed. Guess at sane defaults for the layer boundaries
                    layers = CreateDefaultLayers(j2kData.Length);
                    return false;
                }
                else //Don't save the corrupt texture!
                {
                    // Cache Decoded layers
                    SaveFileCacheForAsset(assetID, layers);
                }
            }
            
            // Notify Interested Parties
            lock (m_notifyList)
            {
                if (m_notifyList.ContainsKey(assetID))
                {
                    foreach (DecodedCallback d in m_notifyList[assetID])
                    {
                        if (d != null)
                            d.DynamicInvoke(assetID, layers);
                    }
                    m_notifyList.Remove(assetID);
                }
            }
            return true;
        }

        private OpenJPEG.J2KLayerInfo[] CreateDefaultLayers(int j2kLength)
        {
            OpenJPEG.J2KLayerInfo[] layers = new OpenJPEG.J2KLayerInfo[5];

            for (int i = 0; i < layers.Length; i++)
                layers[i] = new OpenJPEG.J2KLayerInfo();

            // These default layer sizes are based on a small sampling of real-world texture data
            // with extra padding thrown in for good measure. This is a worst case fallback plan
            // and may not gracefully handle all real world data
            layers[0].Start = 0;
            layers[1].Start = (int)((float)j2kLength * 0.02f);
            layers[2].Start = (int)((float)j2kLength * 0.05f);
            layers[3].Start = (int)((float)j2kLength * 0.20f);
            layers[4].Start = (int)((float)j2kLength * 0.50f);

            layers[0].End = layers[1].Start - 1;
            layers[1].End = layers[2].Start - 1;
            layers[2].End = layers[3].Start - 1;
            layers[3].End = layers[4].Start - 1;
            layers[4].End = j2kLength;

            return layers;
        }

        private void SaveFileCacheForAsset(UUID AssetId, OpenJPEG.J2KLayerInfo[] Layers)
        {
            if(m_useCache)
                m_decodedCache.AddOrUpdate(AssetId, Layers, TimeSpan.FromMinutes(10));

            if (m_cache != null)
            {
                string assetID = "j2kCache_" + AssetId.ToString();

                AssetBase layerDecodeAsset = new AssetBase(assetID, assetID, (sbyte)AssetType.Notecard, m_scene.RegionInfo.RegionID.ToString());
                layerDecodeAsset.Local = true;
                layerDecodeAsset.Temporary = true;

                #region Serialize Layer Data

                StringBuilder stringResult = new StringBuilder();
                string strEnd = "\n";
                for (int i = 0; i < Layers.Length; i++)
                {
                    if (i == Layers.Length - 1)
                        strEnd = String.Empty;

                    stringResult.AppendFormat("{0}|{1}|{2}{3}", Layers[i].Start, Layers[i].End, Layers[i].End - Layers[i].Start, strEnd);
                }

                layerDecodeAsset.Data = Util.UTF8.GetBytes(stringResult.ToString());

                #endregion Serialize Layer Data

                m_cache.Cache(layerDecodeAsset);
            }
        }

        bool TryLoadCacheForAsset(UUID AssetId, out OpenJPEG.J2KLayerInfo[] Layers)
        {
            if (m_decodedCache.TryGetValue(AssetId, out Layers))
            {
                return true;
            }
            else if (m_cache != null)
            {
                string assetName = "j2kCache_" + AssetId.ToString();
                AssetBase layerDecodeAsset = m_cache.Get(assetName);

                if (layerDecodeAsset != null)
                {
                    #region Deserialize Layer Data

                    string readResult = Util.UTF8.GetString(layerDecodeAsset.Data);
                    string[] lines = readResult.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

                    if (lines.Length == 0)
                    {
                        m_log.Warn("[J2KDecodeCache]: Expiring corrupted layer data (empty) " + assetName);
                        m_cache.Expire(assetName);
                        return false;
                    }

                    Layers = new OpenJPEG.J2KLayerInfo[lines.Length];

                    for (int i = 0; i < lines.Length; i++)
                    {
                        string[] elements = lines[i].Split('|');
                        if (elements.Length == 3)
                        {
                            int element1, element2;

                            try
                            {
                                element1 = Convert.ToInt32(elements[0]);
                                element2 = Convert.ToInt32(elements[1]);
                            }
                            catch (FormatException)
                            {
                                m_log.Warn("[J2KDecodeCache]: Expiring corrupted layer data (format) " + assetName);
                                m_cache.Expire(assetName);
                                return false;
                            }

                            Layers[i] = new OpenJPEG.J2KLayerInfo();
                            Layers[i].Start = element1;
                            Layers[i].End = element2;
                        }
                        else
                        {
                            m_log.Warn("[J2KDecodeCache]: Expiring corrupted layer data (layout) " + assetName);
                            m_cache.Expire(assetName);
                            return false;
                        }
                    }

                    #endregion Deserialize Layer Data

                    return true;
                }
            }

            return false;
        }
    }
}
