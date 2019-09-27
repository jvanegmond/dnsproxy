using System;
using System.Collections.Generic;
using System.Management;

namespace DnsProxy
{
    public class NetworkInterface : IEquatable<NetworkInterface>, IEqualityComparer<NetworkInterface>
    {
        public NetworkInterface()
        {
        }

        public NetworkInterface(ManagementObject networkAdapter)
        {
            Description = (string)networkAdapter["Description"];
            MacAddress = (string)networkAdapter["MACAddress"];
            IpAddress = (string[])networkAdapter["IPAddress"];
        }

        public string Description { get; set; }
        public string MacAddress { get; set; }
        public string[] IpAddress { get; set; }


        public bool Equals(NetworkInterface x, NetworkInterface y)
        {
            if (x == null || y == null) return false;
            return x.Equals(y);
        }

        public int GetHashCode(NetworkInterface obj)
        {
            return obj.GetHashCode();
        }

        public bool Equals(NetworkInterface other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return string.Equals(MacAddress, other.MacAddress);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((NetworkInterface) obj);
        }

        public override int GetHashCode()
        {
            return (MacAddress != null ? MacAddress.GetHashCode() : 0);
        }
    }
}