using ProtoBuf;
using System.Xml.Serialization;

namespace avaness.PluginLoaderTool.Data
{
    [ProtoContract]
    public class ModPlugin : PluginData
    {
        [XmlIgnore]
        public ulong WorkshopId { get; private set; }

        public override string Id
        {
            get
            {
                return base.Id;
            }
            set
            {
                base.Id = value;
                WorkshopId = ulong.Parse(Id);
            }
        
        }

        [ProtoMember(1)]
        [XmlArray]
        [XmlArrayItem("Id")]
        public ulong[] DependencyIds { get; set; } = new ulong[0];

        [XmlIgnore]
        public ModPlugin[] Dependencies { get; set; } = new ModPlugin[0];

        public ModPlugin()
        { }


    }
}
