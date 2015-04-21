﻿// Copyright (c) IxMilia.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.IO;
using System.Linq;
using IxMilia.Dxf.Blocks;
using IxMilia.Dxf.Entities;
using IxMilia.Dxf.Sections;
using Xunit;

namespace IxMilia.Dxf.Test
{
    public class DxfReaderWriterTests : AbstractDxfTests
    {
        [Fact]
        public void BinaryReaderTest()
        {
            // this file contains 12 lines
            var stream = new FileStream("diamond-bin.dxf", FileMode.Open, FileAccess.Read);
            var file = DxfFile.Load(stream);
            Assert.Equal(12, file.Entities.Count);
            Assert.Equal(12, file.Entities.Where(e => e.EntityType == DxfEntityType.Line).Count());
        }

        [Fact]
        public void SkipBomTest()
        {
            using (var stream = new MemoryStream())
            using (var writer = new StreamWriter(stream))
            {
                writer.Write((char)0xFEFF); // BOM
                writer.Write("0\r\nEOF");
                writer.Flush();
                stream.Seek(0, SeekOrigin.Begin);
                var file = DxfFile.Load(stream);
                Assert.Equal(0, file.Layers.Count);
            }
        }

        [Fact]
        public void ReadThumbnailTest()
        {
            var file = Section("THUMBNAILIMAGE", @" 90
3
310
012345");
            AssertArrayEqual(file.RawThumbnail, new byte[] { 0x01, 0x23, 0x45 });
        }

        [Fact]
        public void WriteThumbnailTestR14()
        {
            var file = new DxfFile();
            file.Header.Version = DxfAcadVersion.R14;
            file.RawThumbnail = new byte[] { 0x01, 0x23, 0x45 };
            VerifyFileDoesNotContain(file, @"  0
SECTION
  2
THUMBNAILIMAGE");
        }

        [Fact]
        public void WriteThumbnailTestR2000()
        {
            var file = new DxfFile();
            file.Header.Version = DxfAcadVersion.R2000;
            file.RawThumbnail = new byte[] { 0x01, 0x23, 0x45 };
            VerifyFileContains(file, @"  0
SECTION
  2
THUMBNAILIMAGE
 90
3
310
012345
  0
ENDSEC");
        }

        [Fact]
        public void WriteThumbnailTest_SetThumbnailBitmap()
        {
            var file = new DxfFile();
            file.Header.Version = DxfAcadVersion.R2000;
            var header = DxfThumbnailImageSection.BITMAPFILEHEADER;
            var bitmap = header.Concat(new byte[] { 0x01, 0x23, 0x45 }).ToArray();
            file.SetThumbnailBitmap(bitmap);
            VerifyFileContains(file, @"  0
SECTION
  2
THUMBNAILIMAGE
 90
3
310
012345
  0
ENDSEC");
        }

        [Fact]
        public void ReadThumbnailTest_GetThumbnailBitmap()
        {
            var file = Section("THUMBNAILIMAGE", @" 90
3
310
012345");
            var expected = new byte[]
            {
                (byte)'B', (byte)'M', // magic number
                0x03, 0x00, 0x00, 0x00, // file length excluding header
                0x00, 0x00, // reserved
                0x00, 0x00, // reserved
                0x36, 0x04, 0x00, 0x00, // bit offset; always 1078
                0x01, 0x23, 0x45 // body
            };
            var bitmap = file.GetThumbnailBitmap();
            AssertArrayEqual(expected, bitmap);
        }

        [Fact]
        public void ReadVersionSpecificClassTest_R13()
        {
            var file = Parse(@"
  0
SECTION
  2
HEADER
  9
$ACADVER
  1
AC1012
  0
ENDSEC
  0
SECTION
  2
CLASSES
  0
<class dxf name>
  1
CPP_CLASS_NAME
  2
<application name>
 90
42
  0
ENDSEC
  0
EOF
");
            Assert.Equal(1, file.Classes.Count);

            var cls = file.Classes.Single();
            Assert.Equal(cls.ClassDxfRecordName, "<class dxf name>");
            Assert.Equal(cls.CppClassName, "CPP_CLASS_NAME");
            Assert.Equal(cls.ApplicationName, "<application name>");
            Assert.Equal(cls.ClassVersionNumber, 42);
        }

        [Fact]
        public void ReadVersionSpecificClassTest_R14()
        {
            var file = Parse(@"
  0
SECTION
  2
HEADER
  9
$ACADVER
  1
AC1014
  0
ENDSEC
  0
SECTION
  2
CLASSES
  0
CLASS
  1
<class dxf name>
  2
CPP_CLASS_NAME
  3
<application name>
 90
42
  0
ENDSEC
  0
EOF
");
            Assert.Equal(1, file.Classes.Count);

            var cls = file.Classes.Single();
            Assert.Equal(cls.ClassDxfRecordName, "<class dxf name>");
            Assert.Equal(cls.CppClassName, "CPP_CLASS_NAME");
            Assert.Equal(cls.ApplicationName, "<application name>");
            Assert.Equal(cls.ProxyCapabilities.Value, 42);
        }

        [Fact]
        public void ReadVersionSpecificBlockRecordTest_R2000()
        {
            var file = Section("TABLES", @"
  0
TABLE
  2
BLOCK_RECORD
  5
2
330
0
100
AcDbSymbolTable
 70
0
  0
BLOCK_RECORD
  5
A
330
0
100
AcDbSymbolTableRecord
100
AcDbBlockTableRecord
  2
<name>
340
A1
310
010203040506070809
310
010203040506070809
1001
ACAD
1000
DesignCenter Data
1002
{
1070
0
1070
1
1070
2
1002
}
  0
ENDTAB
");
            var blockRecord = file.BlockRecords.Single();
            Assert.Equal("<name>", blockRecord.Name);
            Assert.Equal(0xA1u, blockRecord.LayoutHandle);
            Assert.Equal("ACAD", blockRecord.XDataApplicationName);
            Assert.Equal("DesignCenter Data", blockRecord.XDataStringData);
            AssertArrayEqual(new byte[]
            {
                0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09,
                0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09
            }, blockRecord.BitmapData);
        }

        [Fact]
        public void WriteVersionSpecificBlockRecordTest_R2000()
        {
            var file = new DxfFile();
            file.Header.Version = DxfAcadVersion.R2000;
            var blockRecord = new DxfBlockRecord()
            {
                Name = "<name>",
                XDataApplicationName = "ACAD",
                XDataStringData = "DesignCenter Data",
                BitmapData = new byte[]
                {
                    0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09,
                    0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09
                }
            };
            file.BlockRecords.Add(blockRecord);
            VerifyFileContains(file, @"
  0
TABLE
  2
BLOCK_RECORD
  5
2
330
0
100
AcDbSymbolTable
 70
0
  0
BLOCK_RECORD
  5
A
330
0
100
AcDbSymbolTableRecord
100
AcDbBlockTableRecord
  2
<name>
340
0
310
010203040506070809010203040506070809
1001
ACAD
1000
DesignCenter Data
  0
ENDTAB
");
        }

        [Fact]
        public void WriteVersionSpecificClassTest_R13()
        {
            var file = new DxfFile();
            file.Header.Version = DxfAcadVersion.R13;
            file.Classes.Add(new DxfClass()
            {
                ClassDxfRecordName = "<class dxf name>",
                CppClassName = "CPP_CLASS_NAME",
                ApplicationName = "<application name>",
                ClassVersionNumber = 42
            });
            VerifyFileContains(file, @"
  0
SECTION
  2
CLASSES
  0
<class dxf name>
  1
CPP_CLASS_NAME
  2
<application name>
 90
42
");
        }

        [Fact]
        public void WriteVersionSpecificClassTest_R14()
        {
            var file = new DxfFile();
            file.Header.Version = DxfAcadVersion.R14;
            file.Classes.Add(new DxfClass()
            {
                ClassDxfRecordName = "<class dxf name>",
                CppClassName = "CPP_CLASS_NAME",
                ApplicationName = "<application name>",
                ProxyCapabilities = new DxfProxyCapabilities(42)
            });
            VerifyFileContains(file, @"
  0
SECTION
  2
CLASSES
  0
CLASS
  1
<class dxf name>
  2
CPP_CLASS_NAME
  3
<application name>
 90
42
");
        }

        [Fact]
        public void ReadVersionSpecificBlockTest_R13()
        {
            var file = Parse(@"
  0
SECTION
  2
HEADER
  9
$ACADVER
  1
AC1012
  0
ENDSEC
  0
SECTION
  2
BLOCKS
  0
BLOCK
  5
42
100
AcDbEntity
  8
<layer>
100
AcDbBlockBegin
  2
<block name>
 70
0
 10
11
 20
22
 30
33
  3
<block name>
  1
<xref path>
  0
POINT
 10
1.1
 20
2.2
 30
3.3
  0
ENDBLK
  5
42
100
AcDbEntity
  8
<layer>
100
AcDbBlockEnd
  0
ENDSEC
  0
EOF
");

            var block = file.Blocks.Single();
            Assert.Equal("<block name>", block.Name);
            Assert.Equal(0x42u, block.Handle);
            Assert.Equal("<layer>", block.Layer);
            Assert.Equal(11, block.BasePoint.X);
            Assert.Equal(22, block.BasePoint.Y);
            Assert.Equal(33, block.BasePoint.Z);
            var point = (DxfModelPoint)block.Entities.Single();
            Assert.Equal(1.1, point.Location.X);
            Assert.Equal(2.2, point.Location.Y);
            Assert.Equal(3.3, point.Location.Z);
        }

        [Fact]
        public void ReadVersionSpecificBlockTest_R14()
        {
            var file = Parse(@"
  0
SECTION
  2
HEADER
  9
$ACADVER
  1
AC1014
  0
ENDSEC
  0
SECTION
  2
BLOCKS
  0
BLOCK
  5
42
100
AcDbEntity
  8
<layer>
100
AcDbBlockBegin
  2
<block name>
 70
0
 10
11
 20
22
 30
33
  3
<block name>
  1
<xref>
  0
POINT
 10
1.1
 20
2.2
 30
3.3
  0
ENDBLK
  5
42
100
AcDbBlockEnd
  0
ENDSEC
  0
EOF
");

            var block = file.Blocks.Single();
            Assert.Equal("<block name>", block.Name);
            Assert.Equal(0x42u, block.Handle);
            Assert.Equal("<layer>", block.Layer);
            Assert.Equal("<xref>", block.XrefName);
            Assert.Equal(11, block.BasePoint.X);
            Assert.Equal(22, block.BasePoint.Y);
            Assert.Equal(33, block.BasePoint.Z);
            var point = (DxfModelPoint)block.Entities.Single();
            Assert.Equal(1.1, point.Location.X);
            Assert.Equal(2.2, point.Location.Y);
            Assert.Equal(3.3, point.Location.Z);
        }

        [Fact]
        public void WriteVersionSpecificBlockTest_R13()
        {
            var file = new DxfFile();
            file.Header.Version = DxfAcadVersion.R13;
            var block = new DxfBlock();
            block.Name = "<block name>";
            block.Handle = 0x42u;
            block.Layer = "<layer>";
            block.XrefName = "<xref>";
            block.BasePoint = new DxfPoint(11, 22, 33);
            block.Entities.Add(new DxfModelPoint(new DxfPoint(111, 222, 333)));
            file.Blocks.Add(block);
            VerifyFileContains(file, @"
  0
SECTION
  2
BLOCKS
  0
BLOCK
  5
42
330
0
100
AcDbEntity
  8
<layer>
100
AcDbBlockBegin
  2
<block name>
 70
0
 10
11.0
 20
22.0
 30
33.0
  3
<block name>
  1
<xref>
");
            VerifyFileContains(file, @"
 10
111.0
 20
222.0
 30
333.0
");
            VerifyFileContains(file, @"
  0
ENDBLK
  5
42
100
AcDbEntity
  8
<layer>
100
AcDbBlockEnd
  0
ENDSEC
");
        }

        [Fact]
        public void ReadTableTest()
        {
            // sample pulled from R13 spec
            var file = Parse(@"
  0
SECTION
  2
TABLES
  0
TABLE
  2
STYLE
  5
1C
 70
3
1001
APP_X
1040
42.0
  0
STYLE
  5
3A
  2
ENTRY_1
 70
64
 40
0.4
 41
1.0
 50
0.0
 71
0
 42
0.4
  3
BUFONTS.TXT
  0
STYLE
  5
C2
  2
ENTRY_2
  3
BUFONTS.TXT
1001
APP_1
1070
45
1001
APP_2
1004
18A5B3EF2C199A
  0
ENDSEC
  0
EOF
");
            var styleTable = file.TablesSection.StyleTable;
            Assert.Equal(0x1Cu, styleTable.Handle);
            Assert.Equal(3, styleTable.MaxEntries);

            var style1 = file.Styles.First();
            Assert.Equal(0x3Au, style1.Handle);
            Assert.Equal("ENTRY_1", style1.Name);
            Assert.Equal(64, style1.StandardFlags);
            Assert.Equal(0.4, style1.TextHeight);
            Assert.Equal(1.0, style1.WidthFactor);
            Assert.Equal(0.0, style1.ObliqueAngle);
            Assert.Equal(0, style1.TextGenerationFlags);
            Assert.Equal(0.4, style1.LastHeightUsed);
            Assert.Equal("BUFONTS.TXT", style1.PrimaryFontFileName);

            var style2 = file.Styles.Skip(1).Single();
            Assert.Equal(0xC2u, style2.Handle);
            Assert.Equal("ENTRY_2", style2.Name);
            Assert.Equal("BUFONTS.TXT", style2.PrimaryFontFileName);
        }

        [Fact]
        public void WriteVersionSpecificSectionsTest_R12()
        {
            var file = new DxfFile();
            file.Header.Version = DxfAcadVersion.R12;
            file.Classes.Add(new DxfClass());

            // no CLASSES section in R12
            VerifyFileDoesNotContain(file, @"
  0
SECTION
  2
CLASSES
");

            // no OBJECTS section in R12
            VerifyFileDoesNotContain(file, @"
  0
SECTION
  2
OBJECTS
");
        }

        [Fact]
        public void WriteVersionSpecificSectionsTest_R13()
        {
            var file = new DxfFile();
            file.Header.Version = DxfAcadVersion.R13;
            file.Classes.Add(new DxfClass());

            // CLASSES section added in R13
            VerifyFileContains(file, @"
  0
SECTION
  2
CLASSES
");

            // OBJECTS section added in R13
            // NYI
//            VerifyFileContains(file, @"
//  0
//SECTION
//  2
//OBJECTS
//");
        }

        [Fact]
        public void WriteVersionSpecificBlockRecordTest_R12()
        {
            var file = new DxfFile();
            file.Header.Version = DxfAcadVersion.R12;
            file.BlockRecords.Add(new DxfBlockRecord());

            // no BLOCK_RECORD in R12
            VerifyFileDoesNotContain(file, @"
  0
TABLE
  2
BLOCK_RECORD
");
        }

        [Fact]
        public void WriteVersionSpecificBlockRecordTest_R13()
        {
            var file = new DxfFile();
            file.Header.Version = DxfAcadVersion.R13;
            file.BlockRecords.Add(new DxfBlockRecord());

            // BLOCK_RECORD added in R13
            VerifyFileContains(file, @"
  0
TABLE
  2
BLOCK_RECORD
");
        }

        [Fact]
        public void Code280ShortInsteadOfCode290BoolTest()
        {
            // the spec says header variables $HIDETEXT, $INTERSECTIONDISPLAY,  and $XCLIPFRAME should be code 290
            // bools but some R2010 files encountered in the wild have a code 280 short instead

            // first test code 290 bool
            var file = Section("HEADER", @"
  9
$ACADVER
  1
AC1018
  9
$HIDETEXT
290
1
  9
$INTERSECTIONDISPLAY
290
1
  9
$XCLIPFRAME
290
1
");
            Assert.True(file.Header.HideTextObjectsWhenProducintHiddenView);
            Assert.True(file.Header.DisplayIntersectionPolylines);
            Assert.True(file.Header.IsXRefClippingBoundaryVisible);

            // now test code 280 short
            file = Section("HEADER", @"
  9
$ACADVER
  1
AC1018
  9
$HIDETEXT
280
1
  9
$INTERSECTIONDISPLAY
280
1
  9
$XCLIPFRAME
280
1
");
            Assert.True(file.Header.HideTextObjectsWhenProducintHiddenView);
            Assert.True(file.Header.DisplayIntersectionPolylines);
            Assert.True(file.Header.IsXRefClippingBoundaryVisible);

            // verify that these variables aren't written twice
            file = new DxfFile();
            file.Header.Version = DxfAcadVersion.R2004;
            var text = ToString(file);

            Assert.True(text.IndexOf("$HIDETEXT") > 0); // make sure it's there
            Assert.Equal(text.IndexOf("$HIDETEXT"), text.LastIndexOf("$HIDETEXT")); // first and last should be the same

            Assert.True(text.IndexOf("$INTERSECTIONDISPLAY") > 0); // make sure it's there
            Assert.Equal(text.IndexOf("$INTERSECTIONDISPLAY"), text.LastIndexOf("$INTERSECTIONDISPLAY")); // first and last should be the same

            Assert.True(text.IndexOf("$XCLIPFRAME") > 0); // make sure it's there
            Assert.Equal(text.IndexOf("$XCLIPFRAME"), text.LastIndexOf("$XCLIPFRAME")); // first and last should be the same
        }
    }
}
