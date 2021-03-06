﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Aurora.Framework;
using Aurora.DataManager;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Framework;
using Nini.Config;

namespace Aurora.Services.DataService
{
    public class LocalProfileConnector : IProfileConnector, IAuroraDataPlugin
	{
		private Dictionary<UUID, IUserProfileInfo> UserProfilesCache = new Dictionary<UUID, IUserProfileInfo>();
        private IGenericData GD = null;

        public void Initialize(IGenericData GenericData, IConfigSource source, string defaultConnectionString)
        {
            if (source.Configs["AuroraConnectors"].GetString("ProfileConnector", "LocalConnector") == "LocalConnector")
            {
                GD = GenericData;

                if (source.Configs[Name] != null)
                    defaultConnectionString = source.Configs[Name].GetString("ConnectionString", defaultConnectionString);

                GD.ConnectToDatabase(defaultConnectionString);

                DataManager.DataManager.RegisterPlugin(Name, this);
            }
            else
            {
                //Check to make sure that something else exists
                string m_ServerURI = source.Configs["AuroraData"].GetString("RemoteServerURI", "");
                if (m_ServerURI == "") //Blank, not set up
                {
                    OpenSim.Framework.Console.MainConsole.Instance.Output("[AuroraDataService]: Falling back on local connector for " + "ProfileConnector", "None");
                    GD = GenericData;

                    if (source.Configs[Name] != null)
                        defaultConnectionString = source.Configs[Name].GetString("ConnectionString", defaultConnectionString);

                    GD.ConnectToDatabase(defaultConnectionString);

                    DataManager.DataManager.RegisterPlugin(Name, this);
                }
            }
        }

        public string Name
        {
            get { return "IProfileConnector"; }
        }

        public void Dispose()
        {
        }

		public IUserProfileInfo GetUserProfile(UUID agentID)
		{
			IUserProfileInfo UserProfile = new IUserProfileInfo();
            if (UserProfilesCache.TryGetValue(agentID, out UserProfile)) 
				return UserProfile;
            else 
            {
                UserProfile = new IUserProfileInfo();
                List<string> query = null;
                try
                {
                    query = GD.Query(new string[]{"ID", "`Key`"}, new object[]{agentID, "LLProfile"}, "userdata", "Value");
                }
                catch
                {
                }
                if (query == null || query.Count == 0)
					return null;

                OSDMap profile = (OSDMap)OSDParser.DeserializeLLSDXml(query[0]);

				UserProfile.PrincipalID = agentID;
                UserProfile.AllowPublish = profile["AllowPublish"].AsInteger() == 1;
                UserProfile.MaturePublish = profile["MaturePublish"].AsInteger() == 1;
                UserProfile.Partner = profile["Partner"].AsUUID();
                UserProfile.WebURL = profile["WebURL"].AsString();
                UserProfile.AboutText = profile["AboutText"].AsString();
                UserProfile.FirstLifeAboutText = profile["FirstLifeAboutText"].AsString();
                UserProfile.Image = profile["Image"].AsUUID();
                UserProfile.FirstLifeImage = profile["FirstLifeImage"].AsUUID();
                UserProfile.CustomType = profile["CustomType"].AsString();
                UserProfile.Visible = profile["Visible"].AsInteger() == 1;
                UserProfile.IMViaEmail = profile["IMViaEmail"].AsInteger() == 1;
                UserProfile.MembershipGroup = profile["MembershipGroup"].AsString();
                UserProfile.AArchiveName = profile["AArchiveName"].AsString();
                UserProfile.IsNewUser = profile["IsNewUser"].AsInteger() == 1;
                UserProfile.Created = profile["Created"].AsInteger();
                UserProfile.DisplayName = profile["DisplayName"].AsString();
                UserProfile.Interests.CanDoMask = profile["CanDoMask"].AsUInteger();
                UserProfile.Interests.WantToText = profile["WantToText"].AsString();
                UserProfile.Interests.CanDoMask = profile["CanDoMask"].AsUInteger();
                UserProfile.Interests.CanDoText = profile["CanDoText"].AsString();
                UserProfile.Interests.Languages = profile["Languages"].AsString();

                OSD onotes = OSDParser.DeserializeLLSDXml(profile["Notes"].AsString());
                OSDMap notes = onotes.Type == OSDType.Unknown ? new OSDMap() : (OSDMap)onotes;
                UserProfile.Notes = Util.OSDToDictionary(notes);
                OSD opicks = OSDParser.DeserializeLLSDXml(profile["Picks"].AsString());
                OSDMap picks = opicks.Type == OSDType.Unknown ? new OSDMap() : (OSDMap)opicks;
                UserProfile.Picks = Util.OSDToDictionary(picks);
                OSD oclassifieds = OSDParser.DeserializeLLSDXml(profile["Classifieds"].AsString());
                OSDMap classifieds = oclassifieds.Type == OSDType.Unknown ? new OSDMap() : (OSDMap)oclassifieds;
                UserProfile.Classifieds = Util.OSDToDictionary(classifieds);

			    UserProfilesCache[agentID] = UserProfile;

				return UserProfile;
			}
		}

        public bool UpdateUserProfile(IUserProfileInfo Profile)
        {
            Dictionary<string, object> Values = new Dictionary<string, object>();
            Values.Add("AllowPublish", Profile.AllowPublish ? 1 : 0);
            Values.Add("MaturePublish", Profile.MaturePublish ? 1 : 0);
            //Values.Add("Partner");
            Values.Add("WebURL", Profile.WebURL);
            Values.Add("AboutText", Profile.AboutText);
            Values.Add("FirstLifeAboutText", Profile.FirstLifeAboutText);
            Values.Add("Image", Profile.Image);
            Values.Add("FirstLifeImage", Profile.FirstLifeImage);
            //Values.Add("CustomType");
            Values.Add("Visible", Profile.Visible ? 1 : 0);
            Values.Add("IMViaEmail", Profile.IMViaEmail ? 1 : 0);
            Values.Add("MembershipGroup", Profile.MembershipGroup);
            Values.Add("AArchiveName", Profile.AArchiveName);
            Values.Add("IsNewUser", Profile.IsNewUser ? 1 : 0);
            //Values.Add("Created", Profile.Created);
            Values.Add("DisplayName", Profile.DisplayName);
            Values.Add("WantToMask", Profile.Interests.WantToMask);
            Values.Add("WantToText", Profile.Interests.WantToText);
            Values.Add("CanDoMask", Profile.Interests.CanDoMask);
            Values.Add("CanDoText", Profile.Interests.CanDoText);
            Values.Add("Languages", Profile.Interests.Languages);
            Values.Add("Notes", OSDParser.SerializeLLSDXmlString(Util.DictionaryToOSD(Profile.Notes)));
            Values.Add("Picks", OSDParser.SerializeLLSDXmlString(Util.DictionaryToOSD(Profile.Picks)));
            Values.Add("Classifieds", OSDParser.SerializeLLSDXmlString(Util.DictionaryToOSD(Profile.Classifieds)));


            List<object> SetValues = new List<object>();
            List<string> SetRows = new List<string>();
            SetRows.Add("Value");

            OSDMap map = Util.DictionaryToOSD(Values);
            SetValues.Add(OSDParser.SerializeLLSDXmlString(map));

            List<object> KeyValue = new List<object>();
			List<string> KeyRow = new List<string>();
			KeyRow.Add("ID");
            KeyValue.Add(Profile.PrincipalID.ToString());
            KeyRow.Add("`Key`");
            KeyValue.Add("LLProfile");

            //Update cache
            UserProfilesCache[Profile.PrincipalID] = Profile;

            return GD.Update("userdata", SetValues.ToArray(), SetRows.ToArray(), KeyRow.ToArray(), KeyValue.ToArray());
		}

        public void CreateNewProfile(UUID AgentID)
		{
			List<object> values = new List<object>();
            values.Add(AgentID.ToString()); //ID
            values.Add("LLProfile"); //Key
            IUserProfileInfo profile = new IUserProfileInfo();
            profile.PrincipalID = AgentID;
            values.Add(OSDParser.SerializeLLSDXmlString(Util.DictionaryToOSD(profile.ToKeyValuePairs()))); //Value which is a default Profile
			GD.Insert("userdata", values.ToArray());
		}

		public void RemoveFromCache(UUID ID)
		{
			UserProfilesCache.Remove(ID);
		}
    }
}
