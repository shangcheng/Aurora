﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Reflection;
using System.Text;
using System.Threading;
using Aurora.DataManager;
using Mono.Data.SqliteClient;
using Aurora.Framework;

namespace Aurora.DataManager.SQLite
{
    public class SQLiteRegion : SQLiteLoader, IRegionData
    {
        public Dictionary<string, string> GetRegionHidden()
        {
            SqliteCommand cmd = new SqliteCommand();
            string query = "";
            query = String.Format("select RegionHandle,regionName from auroraregions where hidden = '{0}'", "1");
            cmd.CommandText = query;
            IDataReader reader = GetReader(cmd);
            Dictionary<string, string> retval = new Dictionary<string, string>();
            while (reader.Read())
            {
                for (int i = 0; i < reader.FieldCount; i = i + 2)
                {
                    retval.Add(reader.GetValue(i).ToString(), reader.GetValue(i + 1).ToString());
                }
            }
            return retval;
        }

        public string AbuseReports()
        {
            string query = "SELECT ReportNumber FROM reports ORDER BY ReportNumber DESC";
            SqliteCommand cmd = new SqliteCommand();
            cmd.CommandText = query;
            IDataReader reader = GetReader(cmd);
            if (reader.Read())
            {
                return reader.GetString(0);
            }
            else
            {
                return "";
            }
        }
        public ObjectMediaURLInfo[] getObjectMediaInfo(string objectID)
        {
            ObjectMediaURLInfo[] infos = new ObjectMediaURLInfo[6];
            ObjectMediaURLInfo info = new ObjectMediaURLInfo();
            List<string> data = AssetQuery("objectUUID", objectID, "assetMediaURL", "*");
            if (data.Count == 1)
                return infos;
            infos[1] = info;
            int a = 0;
            int b = 0;
            for (int i = 0; i < 6; ++i)
            {
                info.alt_image_enable = data[b + 2];
                info.auto_loop = Convert.ToInt32(data[b + 3]) == 1;
                info.auto_play = Convert.ToInt32(data[b + 4]) == 1;
                info.auto_scale = Convert.ToInt32(data[b + 5]) == 1;
                info.auto_zoom = Convert.ToInt32(data[b + 6]) == 1;
                info.controls = Convert.ToInt32(data[b + 7]);
                info.current_url = data[b + 8];
                info.first_click_interact = Convert.ToInt32(data[b + 9]) == 1;
                info.height_pixels = Convert.ToInt32(data[b + 10]);
                info.home_url = data[b + 11];
                info.perms_control = Convert.ToInt32(data[b + 12]);
                info.perms_interact = Convert.ToInt32(data[b + 13]);
                info.whitelist = data[b + 14];
                info.whitelist_enable = Convert.ToInt32(data[b + 15]) == 1;
                info.width_pixels = Convert.ToInt32(data[b + 16]);
                info.object_media_version = data[b + 17];
                a++;
                if (a == 1)
                    b = 19;
                if (a == 38)
                    b = 38;
                if (a == 57)
                    b = 57;
                if (a == 76)
                    b = 76;
                if (a == 95)
                    b = 95;
            }
            return infos;
        }
        public List<string> AssetQuery(string keyRow, string keyValue, string table, string wantedValue)
        {
            SqliteCommand cmd = new SqliteCommand();
            string query = "";
            if (keyRow == "")
            {
                query = String.Format("select {0} from {1}",
                    wantedValue, table);
            }
            else
            {
                query = String.Format("select {0} from {1} where {2} = '{3}' ORDER BY count ASC",
                    wantedValue, table, keyRow, keyValue);
            }
            cmd.CommandText = query;
            IDataReader reader = GetReader(cmd);
            List<string> RetVal = new List<string>();
            while (reader.Read())
            {
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    RetVal.Add(reader.GetString(i));
                }
            }
            if (RetVal.Count == 0)
                RetVal.Add("");
            reader.Close();
            reader.Dispose();
            CloseReaderCommand(cmd);

            return RetVal;
        }
        public bool GetIsRegionMature(string region)
        {
            string query = "SELECT isMature FROM auroraregions where regionUUID = '" + region + "'";
            SqliteCommand cmd = new SqliteCommand();
            cmd.CommandText = query;
            IDataReader reader = GetReader(cmd);
            if (reader.Read())
                return reader.GetBoolean(0);
            else
                return true;
        }

        public AbuseReport GetAbuseReport(int formNumber)
        {
            AbuseReport report = new AbuseReport();
            List<string> Reports = Query("ReportNumber", formNumber.ToString(), "abusereports", "*");
            int i = 0;
            foreach (string part in Reports)
            {
                if (i == 0)
                    report.Category = part;
                if (i == 1)
                    report.Reporter = part;
                if (i == 2)
                    report.ObjectName = part;
                if (i == 3)
                    report.ObjectUUID = part;
                if (i == 4)
                    report.Abuser = part;
                if (i == 5)
                    report.Location = part;
                if (i == 6)
                    report.Details = part;
                if (i == 7)
                    report.Position = part;
                if (i == 8)
                    report.Estate = part;
                if (i == 9)
                    report.Summary = part;
                if (i == 10)
                    report.ReportNumber = part;
                if (i == 11)
                    report.AssignedTo = part;
                if (i == 12)
                    report.Active = part;
                if (i == 13)
                    report.Checked = part;
                if (i == 14)
                    report.Notes = part;
                i++;
                if (i == 15)
                    i = 0;
            }
            return report;
        }

        public OfflineMessage[] GetOfflineMessages(string agentID)
        {
            List<OfflineMessage> messages = new List<OfflineMessage>();
            List<string> Messages = Query("ToUUID", agentID, "offlinemessages", "*");
            Delete("offlinemessages", new string[] { "ToUUID" }, new string[] { agentID });
            int i = 0;
            OfflineMessage Message = new OfflineMessage();
            foreach (string part in Messages)
            {
                if (i == 0)
                    Message.FromUUID = part;
                if (i == 1)
                    Message.FromName = part;
                if (i == 2)
                    Message.ToUUID = part;
                if (i == 3)
                    Message.Message = part;
                i++;
                if (i == 4)
                {
                    i = 0;
                    messages.Add(Message);
                    Message = new OfflineMessage();
                }
            }
            return messages.ToArray();
        }

        public void AddOfflineMessage(string fromUUID, string fromName, string toUUID, string message)
        {
            Insert("offlinemessages", new string[] { fromUUID,fromName,toUUID,message});
        }
    }
}