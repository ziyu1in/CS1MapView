using CSLMapView.XML;
using System.IO.Compression;
using CS2MapView.Util.CS1;
using System.Text;
using System.Xml.Serialization;

namespace CS2MapView.Import.CS1
{
    public class CS1XMLImporter
    {



        /// <summary>
        /// ノードに接続するセグメントを追加します。このメソッドはインポート時に使用されます。
        /// </summary>
        /// <param name="node"></param>
        /// <param name="s"></param>
        private static void AttachSegmentToNode(XMLNode node, XMLSegment s)
        {
            node.Segments ??= [];
            if (!node.Segments.Contains(s))
            {
                node.Segments.Add(s);
            }
        }

        private static void AddConnectionSegment(XMLSegment seg, XMLSegment add)
        {
            if (seg == null || add == null || seg == add)
            {
                return;
            }
            seg.StartConnections ??= [];
            seg.EndConnections ??= [];
            XMLVector3 segStart = seg.Points[0];
            if (segStart.Equals(add.Points[0]) || segStart.Equals(add.Points.Last()))
            {
                if (!seg.StartConnections.Contains(add))
                {
                    seg.StartConnections.Add(add);
                }
            }
            XMLVector3 segEnd = seg.Points.Last();
            if (segEnd.Equals(add.Points[0]) || segEnd.Equals(add.Points.Last()))
            {
                if (!seg.EndConnections.Contains(add))
                {
                    seg.EndConnections.Add(add);
                }
            }

            add.StartConnections ??= [];
            add.EndConnections ??= [];

            segStart = add.Points[0];
            if (segStart.Equals(seg.Points[0]) || segStart.Equals(seg.Points.Last()))
            {
                if (!add.StartConnections.Contains(seg))
                {
                    add.StartConnections.Add(seg);
                }
            }
            segEnd = add.Points.Last();
            if (segEnd.Equals(seg.Points[0]) || segEnd.Equals(seg.Points.Last()))
            {
                if (!add.EndConnections.Contains(seg))
                {
                    add.EndConnections.Add(seg);
                }
            }
        }
        private static void ProcessNodeSegment(IEnumerable<XMLNode> nodeList, IEnumerable<XMLSegment> segmentList)
        {
            Dictionary<int, XMLSegment> segmentMap = [];
            Dictionary<int, XMLNode> nodeMap = [];
            Dictionary<XMLVector3, List<XMLSegment>> positionGroupedSegmentMap = [];

            foreach (XMLSegment s in segmentList)
            {
                segmentMap.Add(s.Id, s);
            }
            foreach (XMLNode n in nodeList)
            {
                nodeMap.Add(n.Id, n);
            }

            foreach (XMLSegment seg in segmentList)
            {
                //segmentに開始・終了nodeを取り付け
                seg.StartNode = nodeMap[seg.StartNodeId];
                seg.EndNode = nodeMap[seg.EndNodeId];
                AttachSegmentToNode(seg.StartNode, seg);
                AttachSegmentToNode(seg.EndNode, seg);

                XMLVector3 pos1 = seg.Points.First();
                XMLVector3 pos2 = seg.Points.Last();
                if (positionGroupedSegmentMap.TryGetValue(pos1, out var list))
                {
                    list.Add(seg);
                }
                else
                {
                    positionGroupedSegmentMap.Add(pos1, [seg]);
                }
                if (positionGroupedSegmentMap.TryGetValue(pos2, out var list2))
                {
                    list2.Add(seg);
                }
                else
                {
                    positionGroupedSegmentMap.Add(pos2, [seg]);
                }
            }

            //segmentに接続segmentを取り付け

            foreach (var kv in positionGroupedSegmentMap)
            {
                XMLVector3 position = kv.Key;
                List<XMLSegment> tempSegmentList = kv.Value;

                foreach (XMLSegment seg1 in tempSegmentList)
                {
                    foreach (XMLSegment seg2 in tempSegmentList)
                    {
                        AddConnectionSegment(seg1, seg2);
                    }
                }
            }
        }

        private static void ProcessTerrains(CSLExportXML xml)
        {
            xml.LandArray = new float[CSLExportXML.TERRAIN_RESOLUTION * CSLExportXML.TERRAIN_RESOLUTION];
            xml.WaterArray = new float[CSLExportXML.TERRAIN_RESOLUTION * CSLExportXML.TERRAIN_RESOLUTION];
            char[] separatorComma = [','];
            char[] separatorColon = [':'];
            int counter = 0;
            foreach (string line in xml.TerrainLines)
            {
                string[]? entries = line.Split(separatorComma);
                foreach (string entry in entries)
                {
                    string[] lw = entry.Split(separatorColon);

                    xml.LandArray[counter] = int.Parse(lw[0]) / 64f;
                    xml.WaterArray[counter] = int.Parse(lw[1]) / 64f;

                    counter++;
                }
                entries = null;
            }
            // Keep the serialized source intact; these arrays are derived views.

            //森
            List<byte> vegeBytes = [];
            foreach (string line in xml.ForestList)
            {
                string[] entries = line.Split(separatorComma);
                foreach (string val in entries)
                {
                    vegeBytes.Add(byte.Parse(val));
                }
            }
            xml.ForestByteData = vegeBytes.ToArray();
        }

        public static CSLExportXML Import(string fileName)
        {
            var ser = new XmlSerializer(typeof(CSLExportXML));
            CSLExportXML? result = null;

            if (fileName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            {
                using var r = new GZipStream(new FileStream(fileName, FileMode.Open, FileAccess.Read), CompressionMode.Decompress, false);
                result = ser.Deserialize(r) as CSLExportXML;
            }
            else
            {
                using var r = new StreamReader(new FileStream(fileName, FileMode.Open, FileAccess.Read), Encoding.UTF8);
                result = ser.Deserialize(r) as CSLExportXML;
            }

            if (result is null)
                throw new InvalidDataException("The file contains no CS1 map data.");

            // Derive node connections and terrain arrays without simplifying or
            // reversing original segments. Transit and track metadata consume this
            // raw model; any simplified base view must be a separate copy.

            ProcessTerrains(result!);
            //セグメント
            ProcessNodeSegment(result!.NodeList, result.SegmentList);

            return result;

        }

        /// <summary>
        /// Build the upstream simplified street-label view on copied nodes and
        /// segments. Source IDs, PathUnit references, lane metadata and assets
        /// remain intact in the raw model used by transit, tracks and premises.
        /// </summary>
        public static CSLExportXML CreateLabelView(CSLExportXML raw, bool preserveRoadHierarchy = false)
            => CreateLabelView(raw, preserveRoadHierarchy, null);

        internal static CSLExportXML CreateLabelView(CSLExportXML raw, bool preserveRoadHierarchy, CS1RoadGeometry? roadGeometry)
        {
            var network = new CSLExportXML
            {
                FileVersion = raw.FileVersion,
                NodeList = raw.NodeList,
                SegmentList = raw.SegmentList,
                SegmentTypeList = raw.SegmentTypeList
            };
            var serializer = new XmlSerializer(typeof(CSLExportXML));
            using var stream = new MemoryStream();
            serializer.Serialize(stream, network);
            stream.Position = 0;
            var copy = (CSLExportXML)serializer.Deserialize(stream)!;
            ProcessNodeSegment(copy.NodeList, copy.SegmentList);
            roadGeometry?.Initialize(copy);
            SegmentSimplicator.SimplifySegments(copy, true, preserveRoadHierarchy, roadGeometry);
            SegmentSimplicator.SimplifySegments(copy, true, preserveRoadHierarchy, roadGeometry);
            // Label builders only read these source collections.
            copy.BuildingList = raw.BuildingList;
            copy.DistrictList = raw.DistrictList;
            copy.DistrictParkList = raw.DistrictParkList;
            copy.City = raw.City;
            return copy;
        }
    }
}
