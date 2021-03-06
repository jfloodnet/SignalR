﻿// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved. See License.md in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNet.SignalR.Infrastructure;

namespace Microsoft.AspNet.SignalR
{
    public class ScaleoutSubscription : Subscription
    {
        private readonly ConcurrentDictionary<string, Linktionary<ulong, ScaleoutMapping>> _streams;
        private List<Cursor> _cursors;

        public ScaleoutSubscription(string identity,
                                    IEnumerable<string> eventKeys,
                                    string cursor,
                                    ConcurrentDictionary<string, Linktionary<ulong, ScaleoutMapping>> streamMappings,
                                    Func<MessageResult, Task<bool>> callback,
                                    int maxMessages,
                                    IPerformanceCounterManager counters)
            : base(identity, eventKeys, callback, maxMessages, counters)
        {
            _streams = streamMappings;

            IEnumerable<Cursor> cursors = null;

            if (cursor == null)
            {
                cursors = from key in _streams.Keys
                          select new Cursor
                          {
                              Key = key,
                              Id = GetCursorId(key)
                          };
            }
            else
            {
                cursors = Cursor.GetCursors(cursor);
            }

            _cursors = new List<Cursor>(cursors);
        }

        public override string GetCursor()
        {
            return Cursor.MakeCursor(_cursors);
        }

        protected override void PerformWork(ref List<ArraySegment<Message>> items, out string nextCursor, ref int totalCount, out object state)
        {
            // The list of cursors represent (streamid, payloadid)
            var cursors = new List<Cursor>();

            foreach (var stream in _streams)
            {
                // Get the mapping for this stream
                Linktionary<ulong, ScaleoutMapping> mapping = stream.Value;

                // See if we have a cursor for this key
                Cursor cursor = null;

                // REVIEW: We should optimize this
                int index = _cursors.FindIndex(c => c.Key == stream.Key);

                bool consumed = true;

                if (index != -1)
                {
                    cursor = Cursor.Clone(_cursors[index]);

                    // If there's no node for this cursor id it's likely because we've
                    // had an app domain restart and the cursor position is now invalid.
                    if (mapping[cursor.Id] == null)
                    {
                        // Set it to the first id in this mapping
                        cursor.Id = stream.Value.MinKey;

                        // Mark this cursor as unconsumed
                        consumed = false;
                    }
                }
                else
                {
                    // Create a cursor and add it to the list.
                    // Point the Id to the first value
                    cursor = new Cursor
                    {
                        Id = stream.Value.MinKey,
                        Key = stream.Key
                    };

                    consumed = false;
                }

                cursors.Add(cursor);

                // Try to find a local mapping for this payload
                LinkedListNode<KeyValuePair<ulong, ScaleoutMapping>> node = mapping[cursor.Id];

                // Skip this node only if this isn't a new cursor
                if (node != null && consumed)
                {
                    // Skip this node since we've already consumed it
                    node = node.Next;
                }

                while (node != null)
                {
                    KeyValuePair<ulong, ScaleoutMapping> pair = node.Value;

                    // Stop if we got more than max messages
                    if (totalCount >= MaxMessages)
                    {
                        break;
                    }

                    // It should be ok to lock here since groups aren't modified that often
                    lock (EventKeys)
                    {
                        // For each of the event keys we care about, extract all of the messages
                        // from the payload
                        foreach (var eventKey in EventKeys)
                        {
                            LocalEventKeyInfo info;
                            if (pair.Value.EventKeyMappings.TryGetValue(eventKey, out info))
                            {
                                int maxMessages = Math.Min(info.Count, MaxMessages);
                                MessageStoreResult<Message> storeResult = info.Store.GetMessages(info.MinLocal, maxMessages);

                                if (storeResult.Messages.Count > 0)
                                {
                                    items.Add(storeResult.Messages);
                                    totalCount += storeResult.Messages.Count;
                                }
                            }
                        }
                    }

                    // Update the cursor id
                    cursor.Id = pair.Key;
                    node = node.Next;
                }
            }

            nextCursor = Cursor.MakeCursor(cursors);

            state = cursors;
        }

        protected override void BeforeInvoke(object state)
        {
            _cursors = (List<Cursor>)state;
        }

        private ulong GetCursorId(string key)
        {
            Linktionary<ulong, ScaleoutMapping> mapping;
            if (_streams.TryGetValue(key, out mapping))
            {
                return mapping.MaxKey;
            }

            return 0;
        }
    }
}
