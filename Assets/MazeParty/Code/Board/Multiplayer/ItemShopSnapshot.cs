using System;
using MazeParty.Gameplay;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public struct ItemShopSnapshot : INetworkSerializable, IEquatable<ItemShopSnapshot>
    {
        public bool Active;
        public Vector2Int Location;
        public int Revision;
        public int AppearedTurn;
        public byte SoldMask;
        public byte Offer0;
        public byte Offer1;
        public byte Offer2;
        public byte Offer3;
        public byte Offer4;

        public bool IsSoldOut =>
            SoldMask == (1 << ItemShopRules.OfferCount) - 1;

        public PrototypeItemId GetOffer(int offerIndex)
        {
            switch (offerIndex)
            {
                case 0: return (PrototypeItemId)Offer0;
                case 1: return (PrototypeItemId)Offer1;
                case 2: return (PrototypeItemId)Offer2;
                case 3: return (PrototypeItemId)Offer3;
                case 4: return (PrototypeItemId)Offer4;
                default: return PrototypeItemId.None;
            }
        }

        public bool IsSold(int offerIndex)
        {
            return offerIndex < 0 || offerIndex >= ItemShopRules.OfferCount ||
                   (SoldMask & (1 << offerIndex)) != 0;
        }

        public static ItemShopSnapshot Create(
            Vector2Int location,
            int revision,
            int appearedTurn,
            ItemShopStock stock)
        {
            if (stock == null)
            {
                throw new ArgumentNullException(nameof(stock));
            }

            return new ItemShopSnapshot
            {
                Active = true,
                Location = location,
                Revision = revision,
                AppearedTurn = appearedTurn,
                SoldMask = stock.SoldMask,
                Offer0 = (byte)stock.GetOffer(0),
                Offer1 = (byte)stock.GetOffer(1),
                Offer2 = (byte)stock.GetOffer(2),
                Offer3 = (byte)stock.GetOffer(3),
                Offer4 = (byte)stock.GetOffer(4)
            };
        }

        public ItemShopSnapshot WithSoldMask(byte soldMask)
        {
            var copy = this;
            copy.SoldMask = (byte)(soldMask & ((1 << ItemShopRules.OfferCount) - 1));
            return copy;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref Active);
            serializer.SerializeValue(ref Location);
            serializer.SerializeValue(ref Revision);
            serializer.SerializeValue(ref AppearedTurn);
            serializer.SerializeValue(ref SoldMask);
            serializer.SerializeValue(ref Offer0);
            serializer.SerializeValue(ref Offer1);
            serializer.SerializeValue(ref Offer2);
            serializer.SerializeValue(ref Offer3);
            serializer.SerializeValue(ref Offer4);
        }

        public bool Equals(ItemShopSnapshot other)
        {
            return Active == other.Active && Location == other.Location &&
                   Revision == other.Revision && AppearedTurn == other.AppearedTurn &&
                   SoldMask == other.SoldMask && Offer0 == other.Offer0 &&
                   Offer1 == other.Offer1 && Offer2 == other.Offer2 &&
                   Offer3 == other.Offer3 && Offer4 == other.Offer4;
        }

        public override bool Equals(object obj)
        {
            return obj is ItemShopSnapshot other && Equals(other);
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Active);
            hash.Add(Location);
            hash.Add(Revision);
            hash.Add(AppearedTurn);
            hash.Add(SoldMask);
            hash.Add(Offer0);
            hash.Add(Offer1);
            hash.Add(Offer2);
            hash.Add(Offer3);
            hash.Add(Offer4);
            return hash.ToHashCode();
        }
    }
}
