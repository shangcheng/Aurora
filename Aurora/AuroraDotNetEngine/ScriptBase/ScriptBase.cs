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
using System.Runtime.Remoting;
using System.Runtime.Remoting.Lifetime;
using System.Security.Permissions;
using System.Threading;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using log4net;
using Aurora.ScriptEngine.AuroraDotNetEngine;
using Aurora.ScriptEngine.AuroraDotNetEngine.APIs.Interfaces;
using Aurora.ScriptEngine.AuroraDotNetEngine.CompilerTools;

namespace Aurora.ScriptEngine.AuroraDotNetEngine.Runtime
{
    [Serializable]
    public partial class ScriptBaseClass : MarshalByRefObject, IScript, IDisposable
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private ScriptSponsor m_sponser;

        public ISponsor Sponsor
        {
            get
            {
                return m_sponser;
            }
        }

        public override Object InitializeLifetimeService()
        {
            try
            {
                ILease lease = (ILease)base.InitializeLifetimeService();

                if (lease.CurrentState == LeaseState.Initial)
                {
                    // Infinite : lease.InitialLeaseTime = TimeSpan.FromMinutes(0);
                    lease.InitialLeaseTime = TimeSpan.FromMinutes(0);
                    //lease.InitialLeaseTime = TimeSpan.FromMinutes(0);
                    //lease.RenewOnCallTime = TimeSpan.FromMinutes(10.0);
                    //lease.SponsorshipTimeout = TimeSpan.FromMinutes(1.0);
                }
                return lease;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public void UpdateLease(TimeSpan time)
        {
            ILease lease = (ILease)RemotingServices.GetLifetimeService(this as MarshalByRefObject);
            if (lease != null)
                lease.Renew(time);
        }

        #if DEBUG
        // For tracing GC while debugging
        public static bool GCDummy = false;
        ~ScriptBaseClass()
        {
            GCDummy = true;
        }
        #endif

        public ScriptBaseClass()
        {
            m_Executor = new Executor(this);

            m_sponser = new ScriptSponsor();
        }

        public Executor m_Executor = null;

        public long GetStateEventFlags(string state)
        {
            return (long)m_Executor.GetStateEventFlags(state);
        }

        public EnumeratorInfo ExecuteEvent(string state, string FunctionName, object[] args, EnumeratorInfo Start, out Exception ex)
        {
            return m_Executor.ExecuteEvent(state, FunctionName, args, Start, out ex);
        }

        private Dictionary<string, object> m_InitialValues =
                new Dictionary<string, object>();
        private Dictionary<string, FieldInfo> m_Fields =
                new Dictionary<string, FieldInfo>();

        public void InitApi(string api, IScriptApi data)
        {
            MethodInfo mi = GetType().GetMethod("ApiType" + api);
            if (mi == null)
                return;

            ILease lease = (ILease)RemotingServices.GetLifetimeService(data as MarshalByRefObject);
            if (lease != null)
                lease.Register(m_sponser);

            Object[] args = new Object[1];
            args[0] = data;

            mi.Invoke(this, args);

            m_InitialValues = GetVars();
        }

        public virtual void StateChange(string newState)
        {
        }

        public void Close()
        {
            m_sponser.Close();
        }

        public Dictionary<string, object> GetVars()
        {
            Dictionary<string, object> vars = new Dictionary<string, object>();

            if (m_Fields == null)
                return vars;

            m_Fields.Clear();

            Type t = GetType();

            FieldInfo[] fields = t.GetFields(BindingFlags.NonPublic |
                                             BindingFlags.Public |
                                             BindingFlags.Instance |
                                             BindingFlags.DeclaredOnly);

            foreach (FieldInfo field in fields)
            {
                m_Fields[field.Name] = field;

                if (field.FieldType == typeof(LSL_Types.list)) // ref type, copy
                {
                    LSL_Types.list v = (LSL_Types.list)field.GetValue(this);
                    Object[] data = new Object[v.Data.Length];
                    Array.Copy(v.Data, 0, data, 0, v.Data.Length);
                    LSL_Types.list c = new LSL_Types.list();
                    c.Data = data;
                    vars[field.Name] = c;
                }
                else if (field.FieldType == typeof(LSL_Types.LSLInteger) ||
                        field.FieldType == typeof(LSL_Types.LSLString) ||
                        field.FieldType == typeof(LSL_Types.LSLFloat) ||
                        field.FieldType == typeof(Int32) ||
                        field.FieldType == typeof(Double) ||
                        field.FieldType == typeof(Single) ||
                        field.FieldType == typeof(String) ||
                        field.FieldType == typeof(Byte) ||
                        field.FieldType == typeof(short) ||
                        field.FieldType == typeof(LSL_Types.Vector3) ||
                        field.FieldType == typeof(LSL_Types.Quaternion))
                {
                    vars[field.Name] = field.GetValue(this);
                }
            }

            return vars;
        }

        public void SetVars(Dictionary<string, object> vars)
        {
            foreach (KeyValuePair<string, object> var in vars)
            {
                if (m_Fields.ContainsKey(var.Key))
                {
                    try
                    {
                        if (m_Fields[var.Key].FieldType == typeof(LSL_Types.list))
                        {
                            LSL_Types.list v = (LSL_Types.list)m_Fields[var.Key].GetValue(this);
                            Object[] data = (new LSL_Types.list(var.Value.ToString())).Data;
                            v.Data = new Object[data.Length];
                            Array.Copy(data, 0, v.Data, 0, data.Length);
                            m_Fields[var.Key].SetValue(this, v);
                        }
                        else if (m_Fields[var.Key].FieldType == typeof(LSL_Types.LSLInteger))
                        {
                            int val = int.Parse(var.Value.ToString());
                            m_Fields[var.Key].SetValue(this, new LSL_Types.LSLInteger(val));
                        }
                        else if (m_Fields[var.Key].FieldType == typeof(LSL_Types.LSLString))
                        {
                            string val = var.Value.ToString();
                            m_Fields[var.Key].SetValue(this, new LSL_Types.LSLString(val));
                        }
                        else if (m_Fields[var.Key].FieldType == typeof(LSL_Types.LSLFloat))
                        {
                            float val = float.Parse(var.Value.ToString());
                            m_Fields[var.Key].SetValue(this,  new LSL_Types.LSLFloat(val));
                        }
                        else if (m_Fields[var.Key].FieldType == typeof(Int32))
                        {
                            Int32 val = Int32.Parse(var.Value.ToString());
                            m_Fields[var.Key].SetValue(this, val);
                        }
                        else if (m_Fields[var.Key].FieldType == typeof(Double))
                        {
                            Double val = Double.Parse(var.Value.ToString());
                            m_Fields[var.Key].SetValue(this, val);
                        }
                        else if (m_Fields[var.Key].FieldType == typeof(Single))
                        {
                            Single val = Single.Parse(var.Value.ToString());
                            m_Fields[var.Key].SetValue(this, val);
                        }
                        else if (m_Fields[var.Key].FieldType == typeof(String))
                        {
                            String val = var.Value.ToString();
                            m_Fields[var.Key].SetValue(this, val);
                        }
                        else if (m_Fields[var.Key].FieldType == typeof(Byte))
                        {
                            Byte val = Byte.Parse(var.Value.ToString());
                            m_Fields[var.Key].SetValue(this, val);
                        }
                        else if (m_Fields[var.Key].FieldType == typeof(short))
                        {
                            short val = short.Parse(var.Value.ToString());
                            m_Fields[var.Key].SetValue(this, val);
                        }
                        else if (m_Fields[var.Key].FieldType == typeof(LSL_Types.Quaternion))
                        {
                            LSL_Types.Quaternion val = new LSL_Types.Quaternion(var.Value.ToString());
                            m_Fields[var.Key].SetValue(this, val);
                        }
                        else if (m_Fields[var.Key].FieldType == typeof(LSL_Types.Vector3))
                        {
                            LSL_Types.Vector3 val = new LSL_Types.Vector3(var.Value.ToString());
                            m_Fields[var.Key].SetValue(this, val);
                        }
                    }
                    catch (Exception) { }
                }
            }
        }

        public void ResetVars()
        {
            m_Executor.ResetStateEventFlags();
            SetVars(m_InitialValues);
        }

        public void NoOp()
        {
            // Does what is says on the packet. Nowt, nada, nothing.
            // Required for insertion after a jump label to do what it says on the packet!
            // With a bit of luck the compiler may even optimize it out.
        }

        public string  Name 
        {
            get
            {
                return "ScriptBase";
            }
        }
    }
}
