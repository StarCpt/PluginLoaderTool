using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace avaness.PluginLoaderTool.Data
{
    [XmlInclude(typeof(GitHubPlugin))]
    [XmlInclude(typeof(ModPlugin))]
    [ProtoContract]
    [ProtoInclude(103, typeof(GitHubPlugin))]
    [ProtoInclude(104, typeof(ModPlugin))]
    public abstract class PluginData : IEquatable<PluginData>
    {
        [ProtoMember(1)]
        public virtual string Id { get; set; }

        [ProtoMember(2)]
        public string FriendlyName { get; set; } = "Unknown";

        [ProtoMember(3)]
        public bool Hidden { get; set; } = false;

        [ProtoMember(4)]
        public string GroupId { get; set; }

        [ProtoMember(5)]
        public string Tooltip { get; set; }

        [ProtoMember(6)]
        public string Author { get; set; }

        [ProtoMember(7)]
        public string Description { get; set; }

        protected PluginData()
        {
        }


        public override bool Equals(object obj)
        {
            return Equals(obj as PluginData);
        }

        public bool Equals(PluginData other)
        {
            return other != null &&
                   Id == other.Id;
        }

        public override int GetHashCode()
        {
            return 2108858624 + EqualityComparer<string>.Default.GetHashCode(Id);
        }

        public static bool operator ==(PluginData left, PluginData right)
        {
            return EqualityComparer<PluginData>.Default.Equals(left, right);
        }

        public static bool operator !=(PluginData left, PluginData right)
        {
            return !(left == right);
        }

        public override string ToString()
        {
            return Id + '|' + FriendlyName;
        }


    }
}