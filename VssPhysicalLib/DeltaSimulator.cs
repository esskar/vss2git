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

namespace Hpdi.VssPhysicalLib
{
    delegate int FromLogCallback(byte[] data, int offset, int count);
    delegate int FromSuccessorCallback(int offset, int count);

    /// <summary>
    /// Simulates stream-like traversal over a set of revision delta operations.
    /// </summary>
    /// <author>Trevor Robinson</author>
    class DeltaSimulator : IDisposable
    {
        private IEnumerator<DeltaOperation> enumerator;
        private int operationOffset;
        private bool eof;

        public IEnumerable<DeltaOperation> Operations { get; }

        public int Offset { get; private set; }

        public DeltaSimulator(IEnumerable<DeltaOperation> operations)
        {
            this.Operations = operations;
            Reset();
        }

        public void Dispose()
        {
            if (enumerator != null)
            {
                enumerator.Dispose();
                enumerator = null;
            }
        }

        public void Seek(int offset)
        {
            if (offset != Offset)
            {
                if (offset < Offset)
                {
                    Reset();
                }
                while (Offset < offset && !eof)
                {
                    var seekRemaining = offset - Offset;
                    var operationRemaining = enumerator.Current.Length - operationOffset;
                    if (seekRemaining < operationRemaining)
                    {
                        operationOffset += seekRemaining;
                        Offset += seekRemaining;
                    }
                    else
                    {
                        Offset += operationRemaining;
                        eof = !enumerator.MoveNext();
                        operationOffset = 0;
                    }
                }
            }
        }

        public void Read(int length, FromLogCallback fromLog, FromSuccessorCallback fromSuccessor)
        {
            while (length > 0 && !eof)
            {
                var operation = enumerator.Current;
                var operationRemaining = operation.Length - operationOffset;
                var count = Math.Min(length, operationRemaining);
                int bytesRead;
                if (operation.Command == DeltaCommand.WriteLog)
                {
                    bytesRead = fromLog(operation.Data.Array, operation.Data.Offset + operationOffset, count);
                }
                else
                {
                    bytesRead = fromSuccessor(operation.Offset + operationOffset, count);
                }
                if (bytesRead == 0)
                {
                    break;
                }
                operationOffset += bytesRead;
                Offset += bytesRead;
                if (length >= operationRemaining)
                {
                    eof = !enumerator.MoveNext();
                    operationOffset = 0;
                }
                length -= bytesRead;
            }
        }

        private void Reset()
        {
            if (enumerator != null)
            {
                enumerator.Dispose();
            }
            enumerator = Operations.GetEnumerator();
            eof = !enumerator.MoveNext();
            operationOffset = 0;
            Offset = 0;
        }
    }
}
