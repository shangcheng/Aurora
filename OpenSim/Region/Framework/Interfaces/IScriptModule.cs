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
using OpenMetaverse;

namespace OpenSim.Region.Framework.Interfaces
{
    public interface IScriptModule : INonSharedRegionModule
    {
        string ScriptEngineName { get; }

        string GetXMLState(UUID itemID);
        bool SetXMLState(UUID itemID, string xml);

        bool PostScriptEvent(UUID itemID, UUID primID, string name, Object[] args);
        bool PostObjectEvent(UUID itemID, string name, Object[] args);

        // Suspend ALL scripts in a given scene object. The item ID
        // is the UUID of a SOG, and the method acts on all contained
        // scripts. This is different from the suspend/resume that
        // can be issued by a client.
        //
        void SuspendScript(UUID itemID);
        void ResumeScript(UUID itemID);

        ArrayList GetScriptErrors(UUID itemID);

        void UpdateScript(UUID partID, UUID itemID, string script, int startParam, bool postOnRez, int stateSource);
        void StopAllScripts();

        string TestCompileScript(UUID assetID, UUID itemID);

        void UpdateScriptToNewObject(UUID olditemID, OpenSim.Framework.TaskInventoryItem newItem, OpenSim.Region.Framework.Scenes.SceneObjectPart newPart);

        void SaveStateSave(UUID uUID, UUID uUID_2);

        List<string> GetAllFunctionNames();
    }
}
