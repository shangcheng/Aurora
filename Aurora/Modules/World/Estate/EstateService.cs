﻿using System;
using System.Collections.Generic;
using System.Text;
using OpenSim.Region.Framework.Interfaces;
using Aurora.Framework;
using OpenSim.Framework;
using OpenSim.Region.Framework.Scenes;
using OpenMetaverse;
using Nini.Config;

namespace Aurora.Modules
{
    public class EstateSettingsModule : IRegionModule, IEstateSettingsModule
    {
        Scene m_scene;
        IProfileData PD;

        public void Initialise(Scene scene, IConfigSource source)
        {
            scene.RegisterModuleInterface<IEstateSettingsModule>(this);
            m_scene = scene;
        }

        public void PostInitialise()
        {
            PD = Aurora.DataManager.DataManager.GetDefaultProfilePlugin();
        }

        public void Close() { }

        public string Name { get { return "EstateSettingsModule"; } }

        public bool IsSharedModule { get { return true; } }

        public bool AllowTeleport(IScene scene, UUID userID, Vector3 Position, out Vector3 newPosition)
        {
            newPosition = Position;
            EstateSettings ES = ((Scene)scene).EstateService.LoadEstateSettings(scene.RegionInfo.RegionID, false);
            AuroraProfileData Profile = PD.GetProfileInfo(userID);

            if (((Scene)scene).RegionInfo.RegionSettings.Maturity > Profile.Mature)
                return false;

            if (ES.DenyMinors && Profile.Minor)
                return false;

            if (!ES.PublicAccess)
            {
                if (!new List<UUID>(ES.EstateManagers).Contains(userID) || ES.EstateOwner != userID)
                    return false;
            }
            if (!ES.AllowDirectTeleport)
            {
                IGenericData GenericData = Aurora.DataManager.DataManager.GetDefaultGenericPlugin();
                List<string> Telehubs = GenericData.Query("regionUUID", ((Scene)scene).RegionInfo.RegionID.ToString(), "auroraregions", "telehubX,telehubY");
                newPosition = new Vector3(Convert.ToInt32(Telehubs[0]), Convert.ToInt32(Telehubs[1]), Position.Z);
            }
            ILandObject ILO = ((Scene)scene).LandChannel.GetLandObject(Position.X, Position.Y);
            if (ILO.LandData.LandingType == 2)
            {
                List<ILandObject> Parcels = ParcelsNearPoint(((Scene)scene), Position, ILO);
                if (Parcels.Count == 0)
                {
                    ScenePresence SP;
                    ((Scene)scene).TryGetScenePresence(userID, out SP);
                    newPosition = GetNearestRegionEdgePosition(SP);
                }
                else
                    newPosition = Parcels[0].LandData.UserLocation;
            }
            if (ILO.LandData.LandingType == 1)
                newPosition = ILO.LandData.UserLocation;


            return true;
        }

        private Vector3 GetPositionAtGround(Scene scene, float x, float y)
        {
            return new Vector3(x, y, GetGroundHeight(scene, x, y));
        }

        public float GetGroundHeight(Scene scene, float x, float y)
        {
            if (x < 0)
                x = 0;
            if (x >= scene.Heightmap.Width)
                x = scene.Heightmap.Width - 1;
            if (y < 0)
                y = 0;
            if (y >= scene.Heightmap.Height)
                y = scene.Heightmap.Height - 1;

            Vector3 p0 = new Vector3(x, y, (float)scene.Heightmap[(int)x, (int)y]);
            Vector3 p1 = new Vector3(p0);
            Vector3 p2 = new Vector3(p0);

            p1.X += 1.0f;
            if (p1.X < scene.Heightmap.Width)
                p1.Z = (float)scene.Heightmap[(int)p1.X, (int)p1.Y];

            p2.Y += 1.0f;
            if (p2.Y < scene.Heightmap.Height)
                p2.Z = (float)scene.Heightmap[(int)p2.X, (int)p2.Y];

            Vector3 v0 = new Vector3(p1.X - p0.X, p1.Y - p0.Y, p1.Z - p0.Z);
            Vector3 v1 = new Vector3(p2.X - p0.X, p2.Y - p0.Y, p2.Z - p0.Z);

            v0.Normalize();
            v1.Normalize();

            Vector3 vsn = new Vector3();
            vsn.X = (v0.Y * v1.Z) - (v0.Z * v1.Y);
            vsn.Y = (v0.Z * v1.X) - (v0.X * v1.Z);
            vsn.Z = (v0.X * v1.Y) - (v0.Y * v1.X);
            vsn.Normalize();

            float xdiff = x - (float)((int)x);
            float ydiff = y - (float)((int)y);

            return (((vsn.X * xdiff) + (vsn.Y * ydiff)) / (-1 * vsn.Z)) + p0.Z;
        }

        private Vector3 GetPositionAtAvatarHeightOrGroundHeight(ScenePresence avatar, float x, float y)
        {
            Vector3 ground = GetPositionAtGround(avatar.Scene, x, y);
            if (avatar.AbsolutePosition.Z > ground.Z)
            {
                ground.Z = avatar.AbsolutePosition.Z;
            }
            return ground;
        }

        private Vector3 GetNearestRegionEdgePosition(ScenePresence avatar)
        {
            float xdistance = avatar.AbsolutePosition.X < Constants.RegionSize / 2 ? avatar.AbsolutePosition.X : Constants.RegionSize - avatar.AbsolutePosition.X;
            float ydistance = avatar.AbsolutePosition.Y < Constants.RegionSize / 2 ? avatar.AbsolutePosition.Y : Constants.RegionSize - avatar.AbsolutePosition.Y;

            //find out what vertical edge to go to
            if (xdistance < ydistance)
            {
                if (avatar.AbsolutePosition.X < Constants.RegionSize / 2)
                {
                    return GetPositionAtAvatarHeightOrGroundHeight(avatar, 0.0f, avatar.AbsolutePosition.Y);
                }
                else
                {
                    return GetPositionAtAvatarHeightOrGroundHeight(avatar, Constants.RegionSize, avatar.AbsolutePosition.Y);
                }
            }
            //find out what horizontal edge to go to
            else
            {
                if (avatar.AbsolutePosition.Y < Constants.RegionSize / 2)
                {
                    return GetPositionAtAvatarHeightOrGroundHeight(avatar, avatar.AbsolutePosition.X, 0.0f);
                }
                else
                {
                    return GetPositionAtAvatarHeightOrGroundHeight(avatar, avatar.AbsolutePosition.X, Constants.RegionSize);
                }
            }
        }

        public List<ILandObject> ParcelsNearPoint(Scene scene, Vector3 position, ILandObject currentparcel)
        {
            List<ILandObject> parcelsNear = new List<ILandObject>();
            parcelsNear.Add(currentparcel);
            for (int x = -4; x <= 4; x += 4)
            {
                for (int y = -4; y <= 4; y += 4)
                {
                    ILandObject check = scene.LandChannel.GetLandObject(position.X + x, position.Y + y);
                    if (check != null)
                    {
                        if (!parcelsNear.Contains(check))
                        {
                            parcelsNear.Add(check);
                        }
                    }
                }
            }

            return parcelsNear;
        }
    }
}