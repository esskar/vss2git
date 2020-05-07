﻿/* Copyright 2009 HPDI, LLC
 * 
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 * 
 *     http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections.Generic;
using Hpdi.VssLogicalLib;

namespace Hpdi.Vss2Git
{
    /// <summary>
    /// Represents a single revision to a file or directory.
    /// </summary>
    /// <author>Trevor Robinson</author>
    class Revision
    {
        public DateTime DateTime { get; }

        public string User { get; }

        public VssItemName Item { get; }

        public int Version { get; }

        public string Comment { get; }

        public VssAction Action { get; }

        public Revision(DateTime dateTime, string user, VssItemName item,
            int version, string comment, VssAction action)
        {
            this.DateTime = dateTime;
            this.User = user;
            this.Item = item;
            this.Version = version;
            this.Comment = comment;
            this.Action = action;
        }
    }
}
