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
using System.Collections;
using System.Collections.Generic;
using System.Data;
using OpenMetaverse;
using OpenSim.Framework;
using Mono.Data.SqliteClient;

namespace OpenSim.Data.SQLite
{
    public class SQLiteAuthenticationData : SQLiteFramework, IAuthenticationData
    {
        private string m_Realm;
        private List<string> m_ColumnNames;
        private int m_LastExpire;
        private string m_connectionString;

        private static bool m_initialized = false;

        public SQLiteAuthenticationData(string connectionString, string realm)
                : base(connectionString)
        {
            m_Realm = realm;
            m_connectionString = connectionString;

            if (!m_initialized)
            {
                using (SqliteConnection dbcon = new SqliteConnection(m_connectionString))
                {
                    dbcon.Open();
                    Migration m = new Migration(dbcon, GetType().Assembly, "AuthStore");
                    m.Update();
                }
                m_initialized = true;
            }
        }

        public AuthenticationData Get(UUID principalID)
        {
            AuthenticationData ret = new AuthenticationData();
            ret.Data = new Dictionary<string, object>();

            using (SqliteConnection dbcon = new SqliteConnection(m_connectionString))
            {
                dbcon.Open();
                SqliteCommand cmd = new SqliteCommand("select * from `" + m_Realm + "` where UUID = '" + principalID.ToString() + "'", dbcon);

                IDataReader result = cmd.ExecuteReader();

                if (result.Read())
                {
                    ret.PrincipalID = principalID;

                    if (m_ColumnNames == null)
                    {
                        m_ColumnNames = new List<string>();

                        DataTable schemaTable = result.GetSchemaTable();
                        foreach (DataRow row in schemaTable.Rows)
                            m_ColumnNames.Add(row["ColumnName"].ToString());
                    }

                    foreach (string s in m_ColumnNames)
                    {
                        if (s == "UUID")
                            continue;

                        ret.Data[s] = result[s].ToString();
                    }

                    return ret;
                }
                else
                {
                    return null;
                }
            }
        }

        public bool Store(AuthenticationData data)
        {
            if (data.Data.ContainsKey("UUID"))
                data.Data.Remove("UUID");

            string[] fields = new List<string>(data.Data.Keys).ToArray();
            string[] values = new string[data.Data.Count];
            int i = 0;
            foreach (object o in data.Data.Values)
                values[i++] = o.ToString();

            SqliteCommand cmd = new SqliteCommand();

            string update = "update `"+m_Realm+"` set ";
            bool first = true;
            foreach (string field in fields)
            {
                if (!first)
                    update += ", ";
                update += "`" + field + "` = " + data.Data[field];

                first = false;

            }

            update += " where UUID = '" + data.PrincipalID.ToString() + "'";

            cmd.CommandText = update;

            if (ExecuteNonQuery(cmd) < 1)
            {
                string insert = "insert into `" + m_Realm + "` (`UUID`, `" +
                        String.Join("`, `", fields) +
                        "`) values ('" + data.PrincipalID.ToString() + "', " + String.Join(", '", values) + "')";

                cmd.CommandText = insert;

                if (ExecuteNonQuery(cmd) < 1)
                {
                    cmd.Dispose();
                    return false;
                }
            }

            cmd.Dispose();

            return true;
        }

        public bool SetDataItem(UUID principalID, string item, string value)
        {
            SqliteCommand cmd = new SqliteCommand("update `" + m_Realm +
                    "` set `" + item + "` = " + value + " where UUID = '" + principalID.ToString() + "'");

            if (ExecuteNonQuery(cmd) > 0)
                return true;

            return false;
        }

        public bool SetToken(UUID principalID, string token, int lifetime)
        {
            if (System.Environment.TickCount - m_LastExpire > 30000)
                DoExpire();

            SqliteCommand cmd = new SqliteCommand("insert into tokens (UUID, token, validity) values ('" + principalID.ToString() + 
                "', '" + token + "', datetime('now, 'localtime', '+" + lifetime.ToString() + " minutes'))");

            if (ExecuteNonQuery(cmd) > 0)
            {
                cmd.Dispose();
                return true;
            }

            cmd.Dispose();
            return false;
        }

        public bool CheckToken(UUID principalID, string token, int lifetime)
        {
            if (System.Environment.TickCount - m_LastExpire > 30000)
                DoExpire();

            SqliteCommand cmd = new SqliteCommand("update tokens set validity = datetime('now, 'localtime', '+" + lifetime.ToString() + 
                " minutes') where UUID = '" + principalID.ToString() + "' and token = '" + token + "' and validity > datetime('now', 'localtime')");

            if (ExecuteNonQuery(cmd) > 0)
            {
                cmd.Dispose();
                return true;
            }

            cmd.Dispose();

            return false;
        }

        private void DoExpire()
        {
            SqliteCommand cmd = new SqliteCommand("delete from tokens where validity < datetime('now, 'localtime')");
            ExecuteNonQuery(cmd);

            cmd.Dispose();

            m_LastExpire = System.Environment.TickCount;
        }
    }
}