![MSBuild](https://github.com/stjeong/RefOwner/workflows/MSBuild/badge.svg)
![GitHub](https://img.shields.io/github/license/stjeong/RefOwner)
![GitHub tag (latest by date)](https://img.shields.io/github/v/tag/stjeong/RefOwner)

What is it?
================================

This tool shows list of owners for specific .NET object in GC Heap from full memory dump file.

Supported environments (Tested.)

    * .NET Framework 4.6.1 or later
    * Windows 10 or later

How to use
================================

It will help to track memory-leaked object from dump file. For example, let's try this example,

~~~~
using System;
using System.Collections.Generic;

namespace ConsoleApp1
{
    class Program
    {
        static List<string> _list = new List<string>();

        static void Main(string[] args)
        {
            CreateObjects();
            GC.Collect(2);
            GC.Collect(2);

            Console.WriteLine("Dump!");
            Console.ReadKey();
        }

        private static void CreateObjects()
        {
            for (int i = 0; i < 100000; i ++)
            {
                _list.Add(Guid.NewGuid().ToString());
            }
        }
    }
}
~~~~

Run and take full memory dump file. For analyzing, you can run windbg with SOS extension, then you can find System.String seems to leak with "!dumpheap -stat" command.

~~~~
0:000> !dumpheap -stat
Statistics:
      MT    Count    TotalSize Class Name
719025c4        1           12 System.Collections.Generic.GenericEqualityComparer`1[[System.String, mscorlib]]
71900414        1           12 System.Collections.Generic.ObjectEqualityComparer`1[[System.Type, mscorlib]]
718fde9c        1           16 System.Security.Policy.AssemblyEvidenceFactory
7190be18        1           20 System.IO.Stream+NullStream
71901888        1           20 Microsoft.Win32.SafeHandles.SafeFileHandle
718fdde8        1           20 Microsoft.Win32.SafeHandles.SafePEFileHandle
7190c3dc        1           24 System.IO.TextWriter+SyncTextWriter
71902298        1           24 System.Version
71900994        2           24 System.Int32
71461e00        1           24 System.Collections.Generic.List`1[[System.String, mscorlib]]
7190c2e4        1           28 System.IO.__ConsoleStream
7190c29c        1           28 Microsoft.Win32.Win32Native+InputRecord
...[omitted for brevity]...
71900958        6          452 System.Int32[]
718fff54       22          616 System.RuntimeType
718ff54c        6          626 System.Char[]
7190316c        1          783 System.Byte[]
71901e50        3          924 System.Globalization.CultureData
718fef34        4        17604 System.Object[]
718ff698       19       524868 System.String[]
00f2b548       24       525636      Free
718feb40   100154      8604850 System.String
Total 100299 objects
~~~~

The instances of "System.String" are 100,154, but you can't find the root cause why string was being leaked. In this case, just run RefOwner32/RefOwner64 on the dump file as follows,

~~~~
c:\temp\RefOwner\bin> RefOwner32.exe c:\tmp\ConsoleApp1.dmp System.String
Found CLR Version: v4.7.3260.00
Filesize:  6EF000
Timestamp: 5BB7BCB7
Dac File:  mscordacwks_X86_X86_4.7.3260.00.dll
Local dac location: C:\Windows\Microsoft.NET\Framework\v4.0.30319\mscordacwks.dll

System.String, # of instances: 100034
1 System.AppDomainSetup(1)
31 System.Object[](3)
100003 System.String[](2)
Total: 100035
~~~~

Two System.String[] instances make references onto 100,034 System.String instances. And "/d" switch will show more information.

~~~~
c:\temp\RefOwner\bin> RefOwner32.exe /d c:\tmp\ConsoleApp1.dmp System.String
...[생략]...
System.String, # of instances: 100034
System.String, # of instances: 100034
1 System.AppDomainSetup(1)
        [2b714e0, 1]
31 System.Object[](3)
        [3b71020, 1]
        [3b72300, 2]
        [3b72520, 28]
100003 System.String[](2)
        [2b7156c, 3]
        [3bd5570, 100000]
Total: 100035
~~~~

That's it. Because of 0x3bd5570 object, 100,000 of System.String objects couldn't be collected. Again with 3bd5570 address, RefOwner show the owner of System.String[].

~~~~
c:\temp\RefOwner\bin\x86> RefOwner32.exe /d c:\tmp\ConsoleApp1.dmp 3bd5570
Found CLR Version: v4.7.3260.00
Filesize:  6EF000
Timestamp: 5BB7BCB7
Dac File:  mscordacwks_X86_X86_4.7.3260.00.dll
Local dac location: C:\Windows\Microsoft.NET\Framework\v4.0.30319\mscordacwks.dll

3bd5570, # of instances: 1
1 System.Collections.Generic.List<System.String>(1)
        [2b720a4, 1]
Total: 1
~~~~

Or, at this stage, you can find the GC root with "!gcroot 3bd5570" in windbg.


Change Log
================================

1.0.0.0 - Feb 11, 2019

* Initial checked-in (For Koreans, read [this article](http://www.sysnet.pe.kr/2/0/11809))


Reqeuests or Contributing to Repository
================================
If you need some features or whatever, make an issue at [https://github.com/stjeong/RefOwner/issues](https://github.com/stjeong/RefOwner/issues)

Any help and advices for this repo are welcome.

License
================================
Apache License V2.0

(Refer to LICENSE.txt)
