﻿using System;
using System.Collections.Generic;
using System.Text;
using Aurora.Framework;
using OpenSim.Framework;
using OpenSim.Framework.Console;
using Nini.Config;

namespace Aurora.Services.DataService
{
	//This will always be local, as this is only used by the grid server.
	//The region server should not be using this class.
    public class LocalAvatarArchiverConnector : IAvatarArchiverConnector, IAuroraDataPlugin
	{
		private IGenericData GD = null;

        public void Initialize(IGenericData GenericData, IConfigSource source, string defaultConnectionString)
        {
            if (source.Configs["AuroraConnectors"].GetString("AvatarArchiverConnector", "LocalConnector") == "LocalConnector")
            {
                GD = GenericData;

                if (source.Configs[Name] != null)
                    defaultConnectionString = source.Configs[Name].GetString("ConnectionString", defaultConnectionString);

                GD.ConnectToDatabase(defaultConnectionString);

                List<string> Results = GD.Query("Method", "avatararchives", "Passwords", "Password");
                if (Results.Count == 0)
                {
                    string newPass = MainConsole.Instance.PasswdPrompt("Password to access Avatar Archive");
                    GD.Insert("passwords", new object[] {
					"avatararchives",
					Util.Md5Hash(Util.Md5Hash(newPass))
				});
                }
                DataManager.DataManager.RegisterPlugin(Name, this);
            }
        }

        public string Name
        {
            get { return "IAvatarArchiverConnector"; }
        }

        public void Dispose()
        {
        }

		public AvatarArchive GetAvatarArchive(string Name, string Password)
		{
			if (!CheckPassword(Password))
				return null;
            List<string> RetVal = GD.Query("Name", Name, "avatararchives", "*");
			if (RetVal.Count == 0)
				return null;

			AvatarArchive Archive = new AvatarArchive();
            Archive.Name = RetVal[0];
            Archive.ArchiveXML = RetVal[1];
			return Archive;
		}

		public void SaveAvatarArchive(AvatarArchive archive, string Password)
		{
			if (!CheckPassword(Password))
				return;
            List<string> Check = GD.Query("Name", archive.Name, "avatararchives", "Name");
			if (Check.Count == 0)
            {
                GD.Insert("avatararchives", new object[] {
					archive.Name,
					archive.ArchiveXML
				});
			}
            else
            {
				GD.Update("avatararchives", new object[] { archive.ArchiveXML }, new string[] { "Archive" }, new string[] { "Name" }, new object[] { archive.Name });
			}
		}

		private bool CheckPassword(string Password)
		{
            List<string> TruePassword = GD.Query("Method", "avatararchives", "Passwords", "Password");
            if (TruePassword.Count == 0)
                return false;
            if (Util.Md5Hash(Password) == TruePassword[0])
				return true;
			return false;
		}
	}

}
