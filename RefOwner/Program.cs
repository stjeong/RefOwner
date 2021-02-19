using Microsoft.Diagnostics.Runtime;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RefOwner
{
    // ClrMD를 이용해 메모리 덤프 파일로부터 특정 인스턴스를 참조하고 있는 소유자 확인
    // ; https://www.sysnet.pe.kr/2/0/11809
    class Program
    {
        static string _platformPostfix = (IntPtr.Size == 4) ? "32" : "64";

        static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("RefOwner" + _platformPostfix + " [/d] [dumppath] [typename | address]");
                Console.WriteLine("[Sample]");
                Console.WriteLine("\tRefOwner" + _platformPostfix + " c:\\temp\\test.dmp System.String");
                Console.WriteLine("\tRefOwner" + _platformPostfix + "  /d c:\\temp\\test.dmp System.String");
                return;
            }

            string filePath = (args.Length == 3) ? args[1] : args[0];
            if (File.Exists(filePath) == false)
            {
                Console.WriteLine("FILE-NOT-FOUND: " + filePath);
                return;
            }

            string basePath = Path.GetDirectoryName(filePath);
            string dmpPath = filePath;
            string dacPath = Path.Combine(basePath, "mscordacwks.dll");

            string typeNameOrAddress = (args.Length == 3) ? args[2] : args[1];

            bool detailed = args.Length == 3 && args[0] == "/d";

            using (DataTarget dataTarget = DataTarget.LoadCrashDump(dmpPath))
            {
                foreach (ClrInfo version in dataTarget.ClrVersions)
                {
                    Console.WriteLine("Found CLR Version: " + version.Version);

                    // This is the data needed to request the dac from the symbol server:
                    ModuleInfo dacInfo = version.DacInfo;
                    Console.WriteLine("Filesize:  {0:X}", dacInfo.FileSize);
                    Console.WriteLine("Timestamp: {0:X}", dacInfo.TimeStamp);
                    Console.WriteLine("Dac File:  {0}", dacInfo.FileName);

                    string dacLocation = version.LocalMatchingDac;
                    if (!string.IsNullOrEmpty(dacLocation))
                        Console.WriteLine("Local dac location: " + dacLocation);

                    Console.WriteLine();

                    ClrRuntime runtime;

                    if (File.Exists(dacPath) == false)
                    {
                        runtime = version.CreateRuntime();
                    }
                    else
                    {
                        runtime = version.CreateRuntime(dacPath);
                    }

                    // DumpHeapStat(runtime);
                    // Console.WriteLine();
                    DumpHeapRefHierachy(runtime, typeNameOrAddress, detailed, 0);
                }
            }
        }

        private static void DumpHeapRefHierachy(ClrRuntime runtime, string typeNameOrAddress, bool detailed, int depth)
        {
            HashSet<ulong> instances = GetObjectListByName(runtime, typeNameOrAddress);
            Console.WriteLine(typeNameOrAddress + ", # of instances: " + instances.Count);

            Dictionary<string, HeapObjectCounter> owners = GetObjectOwners(runtime, instances);
            List<ObjectHistogramItem> histogram = new List<ObjectHistogramItem>();

            foreach (var owner in owners)
            {
                ObjectHistogramItem item = new ObjectHistogramItem { Key = owner.Key, Counter = owner.Value };
                histogram.Add(item);
            }

            histogram.Sort();

            int count = 0;

            foreach (var item in histogram)
            {
                Console.WriteLine($"{item.Counter.Total} {item.Key}({item.Counter.OwnerCount})");

                if (detailed == true)
                {
                    foreach (var instance in item.Counter)
                    {
                        Console.WriteLine($"\t[{instance.Key:x}, {instance.Value}]");
                    }
                }

                count += item.Counter.Total;
            }

            Console.WriteLine("Total: " + count);
        }

        private static ulong GetFieldAddressValue(ClrRuntime runtime, ClrInstanceField field, ulong addr)
        {
            ulong fieldAddress = field.GetAddress(addr);
            return ReadMemory(runtime, fieldAddress);
        }

        private static ulong ReadMemory(ClrRuntime runtime, ulong address)
        {
            bool x86 = IntPtr.Size == 4;

            unsafe
            {
                byte[] refAddr = null;

                if (x86 == true)
                {
                    refAddr = new byte[4];
                }
                else
                {
                    refAddr = new byte[8];
                }

                runtime.Heap.ReadMemory(address, refAddr, 0, IntPtr.Size);

                if (x86 == true)
                {
                    return BitConverter.ToUInt32(refAddr, 0);
                }
                else
                {
                    return BitConverter.ToUInt64(refAddr, 0);
                }                    
            }
        }

        private static Dictionary<string, HeapObjectCounter> GetObjectOwners(ClrRuntime runtime, HashSet<ulong> instances)
        {
            Dictionary<string, HeapObjectCounter> dict = new Dictionary<string, HeapObjectCounter>();

            if (!runtime.Heap.CanWalkHeap)
            {
                Console.WriteLine("Cannot walk the heap!");
            }
            else
            {
                foreach (ClrSegment seg in runtime.Heap.Segments)
                {
                    for (ulong obj = seg.FirstObject; obj != 0; obj = seg.NextObject(obj))
                    {
                        ClrType type = runtime.Heap.GetObjectType(obj);

                        if (type == null || type.IsFree == true)
                        {
                            continue;
                        }

                        string typeName = type.ToString();

                        int gen = runtime.Heap.GetGeneration(obj);
                        if (gen != 2)
                        {
                            continue;
                        }

                        if (type.IsArray == true)
                        {
                            int arrayLength = type.GetArrayLength(obj);
                            for (int i = 0; i < arrayLength; i++)
                            {
                                ulong elemAddress = type.GetArrayElementAddress(obj, i);

                                ClrType elemType = type.ComponentType;
                                if (elemType.IsValueClass == false)
                                {
                                    ulong elemRef = ReadMemory(runtime, elemAddress);
                                    if (instances.Contains(elemRef) == true)
                                    {
                                        if (dict.ContainsKey(typeName) == true)
                                        {
                                            dict[typeName].AddCount(obj);
                                        }
                                        else
                                        {
                                            dict.Add(typeName, new HeapObjectCounter(obj));
                                        }
                                    }
                                }
                                else
                                {
                                    AddItem(type, typeName, obj);
                                }
                            }
                        }
                        else
                        {
                            AddItem(type, typeName, obj);
                        }
                    }
                }
            }

            return dict;

            void AddItem(ClrType type, string typeName, ulong objAddress)
            {
                foreach (ClrInstanceField field in type.Fields)
                {
                    ulong fieldAddress = GetFieldAddressValue(runtime, field, objAddress);

                    if (instances.Contains(fieldAddress) == true)
                    {
                        if (dict.ContainsKey(typeName) == true)
                        {
                            dict[typeName].AddCount(objAddress);
                        }
                        else
                        {
                            dict.Add(typeName, new HeapObjectCounter(objAddress));
                        }
                    }
                }
            }
        }

        private static HashSet<ulong> GetObjectListByName(ClrRuntime runtime, string typeNameOrAddress)
        {
            HashSet<ulong> list = new HashSet<ulong>();

            ulong result;
            if (UInt64.TryParse(typeNameOrAddress, System.Globalization.NumberStyles.AllowHexSpecifier, null, out result) == true)
            {
                list.Add(result);
                return list;
            }

            if (!runtime.Heap.CanWalkHeap)
            {
                Console.WriteLine("Cannot walk the heap!");
            }
            else
            {
                foreach (ClrSegment seg in runtime.Heap.Segments)
                {
                    for (ulong obj = seg.FirstObject; obj != 0; obj = seg.NextObject(obj))
                    {
                        ClrType type = runtime.Heap.GetObjectType(obj);

                        if (type == null)
                        {
                            continue;
                        }

                        int gen = runtime.Heap.GetGeneration(obj);
                        if (gen != 2)
                        {
                            continue;
                        }

                        if (type.ToString() != typeNameOrAddress)
                        {
                            continue;
                        }

                        list.Add(obj);
                    }
                }
            }

            return list;
        }

        private static void DumpHeapObject(ClrRuntime runtime)
        {
            if (!runtime.Heap.CanWalkHeap)
            {
                Console.WriteLine("Cannot walk the heap!");
            }
            else
            {
                foreach (ClrSegment seg in runtime.Heap.Segments)
                {
                    for (ulong obj = seg.FirstObject; obj != 0; obj = seg.NextObject(obj))
                    {
                        ClrType type = runtime.Heap.GetObjectType(obj);

                        if (type == null)
                        {
                            continue;
                        }

                        ulong size = type.GetSize(obj);
                        Console.WriteLine("{0,12:X} {1,8:n0} {2,1:n0} {3}", obj, size, seg.GetGeneration(obj), type.Name);
                    }
                }
            }
        }

        private static void DumpHeapStat(ClrRuntime runtime)
        {
            Console.WriteLine("{0,12} {1,12} {2,12} {3,12} {4,4} {5}", "Start", "End", "CommittedEnd", "ReservedEnd", "Heap", "Type");

            foreach (ClrSegment segment in runtime.Heap.Segments)
            {
                string type;
                if (segment.IsEphemeral)
                    type = "Ephemeral";
                else if (segment.IsLarge)
                    type = "Large";
                else
                    type = "Gen2";

                Console.WriteLine("{0,12:X} {1,12:X} {2,12:X} {3,12:X} {4,4} {5}", segment.Start, segment.End, segment.CommittedEnd, segment.ReservedEnd, segment.ProcessorAffinity, type);
            }

            ClrHeap heap = runtime.Heap;
            foreach (var item in (from seg in heap.Segments
                                  group seg by seg.ProcessorAffinity into g
                                  orderby g.Key
                                  select new
                                  {
                                      Heap = g.Key,
                                      Size = g.Sum(p => (uint)p.Length)
                                  }))
            {
                Console.WriteLine("Heap {0,2}: {1:n0} bytes", item.Heap, item.Size);
            }
        }
    }

    public class ObjectHistogramItem : IComparable<ObjectHistogramItem>
    {
        public string Key;
        public HeapObjectCounter Counter;

        public int CompareTo(ObjectHistogramItem other)
        {
            return Counter.Total.CompareTo(other.Counter.Total);
        }
    }

    public class HeapObjectCounter : IEnumerable<KeyValuePair<ulong, int>>
    {
        Dictionary<ulong, int> _owners = new Dictionary<ulong, int>();
        int _total;

        public HeapObjectCounter(ulong address)
        {
            AddCount(address);
        }

        public int OwnerCount
        {
            get
            {
                return _owners.Keys.Count;
            }
        }

        public int Total
        {
            get
            {
                return _total; // _owners.Values.Sum();
            }
        }

        public IEnumerator<KeyValuePair<ulong, int>> GetEnumerator()
        {
            return this._owners.GetEnumerator();
        }

        internal void AddCount(ulong objAddress)
        {
            if (_owners.ContainsKey(objAddress) == true)
            {
                _owners[objAddress]++;
            }
            else
            {
                _owners[objAddress] = 1;
            }

            _total++;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this._owners.GetEnumerator();
        }
    }
}
