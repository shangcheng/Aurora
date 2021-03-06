﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Reflection;
using log4net;
using Mono.Data.Sqlite;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;

namespace OpenSim.Data.SQLite
{
    public class SQLiteRegionData : SQLiteFramework, IRegionData
    {
        private string m_Realm;
        private List<string> m_ColumnNames;

        public SQLiteRegionData(string connectionString, string realm)
                : base(connectionString)
        {
            m_Realm = realm;
            m_connectionString = connectionString;

            using (SqliteConnection dbcon = new SqliteConnection(m_connectionString))
            {
                dbcon.Open();
                Migration m = new Migration(dbcon, GetType().Assembly, "GridStore");
                m.Update();
            }
        }

        public List<RegionData> Get(string regionName, UUID scopeID)
        {
            string command = "select * from '" + m_Realm + "' where regionName like :regionName";
            if (scopeID != UUID.Zero)
                command += " and ScopeID = :scopeID";

            using (SqliteCommand cmd = new SqliteCommand(command))
            {
                cmd.Parameters.AddWithValue(":regionName", regionName);
                cmd.Parameters.AddWithValue(":scopeID", scopeID.ToString());

                return RunCommand(cmd);
            }
        }

        public RegionData Get(int posX, int posY, UUID scopeID)
        {
            string command = "select * from '" + m_Realm + "' where locX = :posX and locY = :posY";
            if (scopeID != UUID.Zero)
                command += " and ScopeID = :scopeID";

            using (SqliteCommand cmd = new SqliteCommand(command))
            {
                cmd.Parameters.AddWithValue(":posX", posX.ToString());
                cmd.Parameters.AddWithValue(":posY", posY.ToString());
                cmd.Parameters.AddWithValue(":scopeID", scopeID.ToString());

                List<RegionData> ret = RunCommand(cmd);
                if (ret.Count == 0)
                    return null;

                return ret[0];
            }
        }

        public List<RegionData> Get(int flags, UUID scopeID)
        {
            string command = "select * from '" + m_Realm + "' where (flags &" + flags.ToString() + ") = " + flags.ToString() + "";
            if (scopeID != UUID.Zero)
                command += " and ScopeID = :scopeID";

            using (SqliteCommand cmd = new SqliteCommand(command))
            {
                cmd.Parameters.AddWithValue(":scopeID", scopeID.ToString());

                return RunCommand(cmd);
            }
        }

        public RegionData Get(UUID regionID, UUID scopeID)
        {
            string command = "select * from '" + m_Realm + "' where uuid = :regionID";
            if (scopeID != UUID.Zero)
                command += " and ScopeID = :scopeID";

            using (SqliteCommand cmd = new SqliteCommand(command))
            {
                cmd.Parameters.AddWithValue(":regionID", regionID.ToString());
                cmd.Parameters.AddWithValue(":scopeID", scopeID.ToString());

                List<RegionData> ret = RunCommand(cmd);
                if (ret.Count == 0)
                    return null;

                return ret[0];
            }
        }

        public List<RegionData> Get(int startX, int startY, int endX, int endY, UUID scopeID)
        {
            string command = "select * from '" + m_Realm + "' where locX between :startX and :endX and locY between :startY and :endY";
            if (scopeID != UUID.Zero)
                command += " and ScopeID = :scopeID";

            using (SqliteCommand cmd = new SqliteCommand(command))
            {
                cmd.Parameters.AddWithValue(":startX", startX.ToString());
                cmd.Parameters.AddWithValue(":startY", startY.ToString());
                cmd.Parameters.AddWithValue(":endX", endX.ToString());
                cmd.Parameters.AddWithValue(":endY", endY.ToString());
                cmd.Parameters.AddWithValue(":scopeID", scopeID.ToString());

                return RunCommand(cmd);
            }
        }

        public List<RegionData> RunCommand(SqliteCommand cmd)
        {
            List<RegionData> retList = new List<RegionData>();

            using (SqliteConnection dbcon = new SqliteConnection(m_connectionString))
            {
                dbcon.Open();
                cmd.Connection = dbcon;

                using (IDataReader result = cmd.ExecuteReader())
                {
                    while (result.Read())
                    {
                        RegionData ret = new RegionData();
                        ret.Data = new Dictionary<string, object>();

                        ret.RegionID = DBGuid.FromDB(result["uuid"]);
                        ret.ScopeID = DBGuid.FromDB(result["ScopeID"]);

                        ret.RegionName = result["regionName"].ToString();
                        ret.posX = Convert.ToInt32(result["locX"]);
                        ret.posY = Convert.ToInt32(result["locY"]);
                        ret.sizeX = Convert.ToInt32(result["sizeX"]);
                        ret.sizeY = Convert.ToInt32(result["sizeY"]);

                        if (m_ColumnNames == null)
                        {
                            m_ColumnNames = new List<string>();

                            DataTable schemaTable = result.GetSchemaTable();
                            foreach (DataRow row in schemaTable.Rows)
                            {
                                if (row["ColumnName"] != null)
                                    m_ColumnNames.Add(row["ColumnName"].ToString());
                            }
                        }

                        foreach (string s in m_ColumnNames)
                        {
                            if (s == "uuid")
                                continue;
                            if (s == "ScopeID")
                                continue;
                            if (s == "regionName")
                                continue;
                            if (s == "locX")
                                continue;
                            if (s == "locY")
                                continue;

                            ret.Data[s] = result[s].ToString();
                        }

                        retList.Add(ret);
                    }
                }
            }

            return retList;
        }

        public bool Store(RegionData data)
        {
            if (data.Data.ContainsKey("uuid"))
                data.Data.Remove("uuid");
            if (data.Data.ContainsKey("ScopeID"))
                data.Data.Remove("ScopeID");
            if (data.Data.ContainsKey("regionName"))
                data.Data.Remove("regionName");
            if (data.Data.ContainsKey("posX"))
                data.Data.Remove("posX");
            if (data.Data.ContainsKey("posY"))
                data.Data.Remove("posY");
            if (data.Data.ContainsKey("sizeX"))
                data.Data.Remove("sizeX");
            if (data.Data.ContainsKey("sizeY"))
                data.Data.Remove("sizeY");
            if (data.Data.ContainsKey("locX"))
                data.Data.Remove("locX");
            if (data.Data.ContainsKey("locY"))
                data.Data.Remove("locY");

            string[] fields = new List<string>(data.Data.Keys).ToArray();

            using (SqliteCommand cmd = new SqliteCommand())
            {
                string update = "update '" + m_Realm + "' set locX=:posX, locY=:posY, sizeX=:sizeX, sizeY=:sizeY";
                foreach (string field in fields)
                {
                    update += ", ";
                    update += "`" + field + "` = :" + field;

                    cmd.Parameters.AddWithValue(":" + field, data.Data[field]);
                }

                update += " where uuid = :regionID";

                if (data.ScopeID != UUID.Zero)
                    update += " and ScopeID = :scopeID";

                cmd.CommandText = update;
                cmd.Parameters.AddWithValue(":regionID", data.RegionID.ToString());
                cmd.Parameters.AddWithValue(":regionName", data.RegionName);
                cmd.Parameters.AddWithValue(":scopeID", data.ScopeID.ToString());
                cmd.Parameters.AddWithValue(":posX", data.posX.ToString());
                cmd.Parameters.AddWithValue(":posY", data.posY.ToString());
                cmd.Parameters.AddWithValue(":sizeX", data.sizeX.ToString());
                cmd.Parameters.AddWithValue(":sizeY", data.sizeY.ToString());
                if (ExecuteNonQuery(cmd) < 1)
                {
                    string insert = "insert into '" + m_Realm + "' ('uuid', 'ScopeID', 'locX', 'locY', 'sizeX', 'sizeY', 'regionName', '" +
                            String.Join("', '", fields) +
                            "') values ( :regionID, :scopeID, :posX, :posY, :sizeX, :sizeY, :regionName, :" + String.Join(", :", fields) + ")";

                    cmd.CommandText = insert;

                    if (ExecuteNonQuery(cmd) < 1)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        public bool SetDataItem(UUID regionID, string item, string value)
        {
            using (SqliteCommand cmd = new SqliteCommand("update '" + m_Realm + "' set '" + item + "' = :" + item + " where uuid = :UUID"))
            {
                cmd.Parameters.AddWithValue(":" + item, value);
                cmd.Parameters.AddWithValue(":UUID", regionID.ToString());

                if (ExecuteNonQuery(cmd) > 0)
                    return true;
            }

            return false;
        }

        public bool Delete(UUID regionID)
        {
            using (SqliteCommand cmd = new SqliteCommand("delete from '" + m_Realm + "' where uuid = :UUID"))
            {
                cmd.Parameters.AddWithValue(":UUID", regionID.ToString());

                if (ExecuteNonQuery(cmd) > 0)
                    return true;
            }

            return false;
        }

        public List<RegionData> GetDefaultRegions(UUID scopeID)
        {
            string command = "select * from '" + m_Realm + "'where (flags & 1) <> 0";
            if (scopeID != UUID.Zero)
                command += " and ScopeID = :scopeID";

            SqliteCommand cmd = new SqliteCommand(command);

            cmd.Parameters.AddWithValue(":scopeID", scopeID.ToString());

            return RunCommand(cmd);
        }

        public List<RegionData> GetFallbackRegions(UUID scopeID, int x, int y)
        {
            string command = "select * from '" + m_Realm + "' where (flags & 2) <> 0";
            if (scopeID != UUID.Zero)
                command += " and ScopeID = :scopeID";

            SqliteCommand cmd = new SqliteCommand(command);

            cmd.Parameters.AddWithValue(":scopeID", scopeID.ToString());

            List<RegionData> regions = RunCommand(cmd);
            RegionDataDistanceCompare distanceComparer = new RegionDataDistanceCompare(x, y);
            regions.Sort(distanceComparer);
            return regions;
        }

        public List<RegionData> GetSafeRegions(UUID scopeID, int x, int y)
        {
            string command = "select * from '" + m_Realm + "' where (flags & 2048) <> 0";
            if (scopeID != UUID.Zero)
                command += " and ScopeID = :scopeID";

            command += " LIMIT 0,10";

            SqliteCommand cmd = new SqliteCommand(command);

            cmd.Parameters.AddWithValue(":scopeID", scopeID.ToString());

            List<RegionData> regions = RunCommand(cmd);
            RegionDataDistanceCompare distanceComparer = new RegionDataDistanceCompare(x, y);
            regions.Sort(distanceComparer);
            return regions;
        }

        #region IRegionData Members


        public List<RegionData> GetHyperlinks(UUID scopeID)
        {
            return Get((int)RegionFlags.Hyperlink, scopeID);
        }

        #endregion
    }
}
