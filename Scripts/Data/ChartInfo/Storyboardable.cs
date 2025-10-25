using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Serialization;

// TODO: REWRITE THIS ASAP

namespace JANOARG.Shared.Data.ChartInfo
{
    [Serializable]
    public class Timestamp : IDeepClonable<Timestamp>
    {
        [FormerlySerializedAs("Time")]
        public BeatPosition Offset;

        public float  Duration;
        public TimestampIDs ID;
        public float  From = float.NaN;
        public float  Target;

        [SerializeReference]
        public IEaseDirective Easing = new BasicEaseDirective(EaseFunction.Linear, EaseMode.In);

        public Timestamp DeepClone()
        {
            var clone = new Timestamp
            {
                Offset = Offset,
                Duration = Duration,
                ID = ID,
                From = From,
                Target = Target,
                Easing = Easing
            };

            return clone;
        }
    }
    
    public class TimestampType
    {
        public TimestampIDs                        ID;
        public string                        Name;
        public Func<Storyboardable, float>   StoryboardGetter;
        public Action<Storyboardable, float> StoryboardSetter;
    }

    [Serializable]
    public class Storyboard : IList<Timestamp>
    {
        public  List<Timestamp>              Timestamps = new();
        private TypeCache _type_cache = TypeCache.Create();

        public int Count => Timestamps.Count;
        public bool IsReadOnly => false;
        public Timestamp this[int index] {
            get
            {
                return Timestamps[index];
            }
            set
            {
                Timestamps[index] = value;
                InvalidateCache();
            }
        }
        public void Add(Timestamp timestamp)
        {
            Timestamps.Add(timestamp);
            Timestamps.Sort((x, y) => x.Offset.CompareTo(y.Offset));

            _type_cache.Invalidate();
        }
        
        public void InvalidateCache()
        {
            _type_cache.Invalidate();
        }

        public Timestamp[] FromType(TimestampIDs type)
        {
            if (!_type_cache.TryGet(type, out Timestamp[] array))
            {
                var ret = new List<Timestamp>();
                for (int i = 0; i < Timestamps.Count; i++)
                {
                    var stmp = Timestamps[i];
                    if (stmp.ID == type)
                    {
                        ret.Add(stmp);
                    }
                }
                array = ret.ToArray();
                _type_cache.Set(type, array);
            }
            return array;
        }
    
        public Storyboard SelfReference()
        {
            var clone = new Storyboard();
            foreach (Timestamp timestamp in Timestamps) 
                clone.Timestamps.Add(timestamp.DeepClone());

            return clone;
        }

        public int IndexOf(Timestamp item)
        {
            return Timestamps.IndexOf(item);
        }
        public void Insert(int index, Timestamp item)
        {
            Timestamps.Insert(index, item);
            InvalidateCache();
        }
        public void RemoveAt(int index)
        {
            Timestamps.RemoveAt(index);
            InvalidateCache();
        }
        public void Clear()
        {
            Timestamps.Clear();
            InvalidateCache();
        }
        public bool Contains(Timestamp item)
        {
            return Timestamps.Contains(item);
        }
        public void CopyTo(Timestamp[] array, int arrayIndex)
        {
            Timestamps.CopyTo(array, arrayIndex);
        }
        public bool Remove(Timestamp item)
        {
            bool result = Timestamps.Remove(item);
            InvalidateCache();
            return result;
        }
        public IEnumerator<Timestamp> GetEnumerator()
        {
            return Timestamps.GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator()
        {
            return Timestamps.GetEnumerator();
        }
        protected struct TypeCache
        {
            static readonly int upper = (int)Enum.GetValues(typeof(TimestampIDs)).Cast<TimestampIDs>().Max();
            static readonly int lower = (int)Enum.GetValues(typeof(TimestampIDs)).Cast<TimestampIDs>().Min();
            Timestamp[][] backing;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static TypeCache Create()
            {
                return new TypeCache
                {
                    backing = new Timestamp[upper + 1 - lower][]
                };
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly bool TryGet(TimestampIDs id, out Timestamp[] ret)
            {
                var idx = (int)id - lower;
                var val = backing[idx];
                if (val is not null)
                {
                    ret = val;
                    return true;
                }
                ret = null;
                return false;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly void Set(TimestampIDs id, Timestamp[] entry)
            {
                backing[(int)id - lower] = entry;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly void Invalidate()
            {
                Array.Clear(backing, 0, upper);
            }
        }
    }

    public abstract class Storyboardable
    {
        internal static readonly Array srTimestampIDValues = Enum.GetValues(typeof(TimestampIDs));
        
        public Storyboard Storyboard = new();

        public abstract TimestampType[] timestampTypes { get; }
        protected Storyboardable cachedDisplayObject;

        public Storyboardable GetStoryboardableObject(float time) 
        {
            cachedDisplayObject ??= (Storyboardable)MemberwiseClone();
            Storyboardable obj = cachedDisplayObject;
            CopyInto(obj);

            foreach (TimestampType timestampType in timestampTypes)
            {
                Timestamp[] storyboard = Storyboard.FromType(timestampType.ID);

                float value = timestampType.StoryboardGetter(this);

                foreach (Timestamp timestamp in storyboard)
                    if (time >= timestamp.Offset + timestamp.Duration)
                        value = timestamp.Target;
                    else if (time > timestamp.Offset)
                    {
                        if (!float.IsNaN(timestamp.From))
                            value = timestamp.From;

                        value = Mathf.LerpUnclamped(
                            value,
                            timestamp.Target,
                            timestamp.Easing.Get((time - timestamp.Offset) / timestamp.Duration)
                        );

                        break;
                    }
                    else
                        break;

                timestampType.StoryboardSetter(obj, value);
            }

            return obj;
        }

        protected float[] CurrentValues;

        protected float CurrentTime;

        /// <remarks>
        /// Let's see if this is redundant
        /// </remarks>
        /// <param name="dst"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public virtual void CopyInto(Storyboardable dst)
        {
            dst.CurrentTime = CurrentTime;
            dst.CurrentValues = CurrentValues;
        }

        public virtual void Advance(float time)
        {
            // Initialize current value of each timestamp type if they don't exist
            if (CurrentValues == null) 
            {
                // Initialize array with size equal to number of enum values
                CurrentValues = new float[srTimestampIDValues.Length];
            
                foreach (TimestampType timestampType in timestampTypes)
                    CurrentValues[(int)timestampType.ID] = timestampType.StoryboardGetter(this);
            }

            // Loop through each timestamp type
            foreach (TimestampType timestampType in timestampTypes)
            {
                // Skip if there isn't any timestamp of the given type, otherwise assign to value
                float value = CurrentValues[(int)timestampType.ID];

                // Navigate forward
                while (true) 
                {
                    // Get the next timestamp in the list
                    Timestamp timestamp = null;
                    
                    foreach (Timestamp storyboardTimestamp in Storyboard.Timestamps)
                    {
                        if (timestampType.ID == storyboardTimestamp.ID)
                        {
                            timestamp = storyboardTimestamp;
                            break;
                        }
                    }
                
                    // Skip if there's no timestamp or it's not yet the start of the next timestamp
                    if (timestamp == null || (time < timestamp.Offset && CurrentTime < timestamp.Offset))
                        break;

                    // If the timestamp is in progress
                    if (time < timestamp.Offset + timestamp.Duration)
                    {
                        // NaN means lerp from the previous value
                        if (!float.IsNaN(timestamp.From))
                            CurrentValues[(int)timestampType.ID] = value = timestamp.From;
                    
                        // Get the current value
                        value = Mathf.LerpUnclamped(value, timestamp.Target, timestamp.Easing.Get((time - timestamp.Offset) / timestamp.Duration));
                    
                        break;
                    }
                    else
                    {
                        // Set value to destination and pop the timestamp off the list
                        CurrentValues[(int)timestampType.ID] = value = timestamp.Target;
                        Storyboard.Timestamps.Remove(timestamp);
                    }
                }
                timestampType.StoryboardSetter(this, value);
            }

            CurrentTime = time;
        }
    }

    public abstract class DirtyTrackedStoryboardable : Storyboardable
    {
        public bool IsDirty;

        // Using dictionaries for faster lookup on higher call volumes
        private Dictionary<TimestampIDs, Queue<Timestamp>> _TimestampsByID;

        // Initialize on storyboard setup
        private void InitializeTimestampGroups()
        {
            _TimestampsByID = new Dictionary<TimestampIDs, Queue<Timestamp>>();
            
            // Group timestamps by ID and sort by offset
            var grouped = Storyboard.Timestamps
                .GroupBy(t => t.ID)
                .ToDictionary(g => g.Key, g => new Queue<Timestamp>(g.OrderBy(t => t.Offset)));
            
            _TimestampsByID = grouped;
        }

        public override void Advance(float time)
        {
            if (CurrentValues == null)
            {
                CurrentValues = new float[srTimestampIDValues.Length];
                InitializeTimestampGroups();

                foreach (TimestampType timestampType in timestampTypes)
                    CurrentValues[(int)timestampType.ID] = timestampType.StoryboardGetter(this);
            }

            foreach (TimestampType timestampType in timestampTypes)
            {
                float value = CurrentValues[(int)timestampType.ID];

                if (!_TimestampsByID.TryGetValue(timestampType.ID, out Queue<Timestamp> timestamps))
                    continue;

                while (timestamps.Count > 0)
                {
                    Timestamp timestamp = timestamps.Peek();

                    // If there's no timestamp or it's not yet the start of the next timestamp
                    if (time < timestamp.Offset && CurrentTime < timestamp.Offset)
                        break;

                    // Otherwise
                    if (time < timestamp.Offset + timestamp.Duration)
                    {
                        // Lerp from previous value if it's not NaN
                        if (!float.IsNaN(timestamp.From))
                            CurrentValues[(int)timestampType.ID] = value = timestamp.From;

                        // Lerp to target value
                        value = Mathf.LerpUnclamped(value, timestamp.Target, timestamp.Easing.Get((time - timestamp.Offset) / timestamp.Duration));
                        IsDirty = true;
                        break;
                    }
                    else
                    {
                        // Set value to target and pop the timestamp off the queue
                        timestamps.Dequeue(); // Much faster than List.Remove()
                        CurrentValues[(int)timestampType.ID] = value = timestamp.Target;
                        IsDirty = true;
                    }
                }
                timestampType.StoryboardSetter(this, value);
            }
            CurrentTime = time;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void CopyInto(Storyboardable dst)
        {
            base.CopyInto(dst);
            var d = (DirtyTrackedStoryboardable)dst;
            d.Storyboard = Storyboard;
            d.IsDirty = IsDirty;
            d._TimestampsByID = _TimestampsByID;
        }
    }
}