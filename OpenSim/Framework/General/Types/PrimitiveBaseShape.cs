using libsecondlife;
using libsecondlife.Packets;

namespace OpenSim.Framework.Types
{
    public enum ShapeType
    {
        Box,
        Sphere,
        Ring,
        Tube,
        Torus,
        Prism,
        Scuplted,
        Cylinder,
        Foliage,
        Unknown
    }

    public class PrimitiveBaseShape
    {
        protected ShapeType type = ShapeType.Unknown;

        public byte PCode;
        public ushort PathBegin;
        public ushort PathEnd;
        public byte PathScaleX;
        public byte PathScaleY;
        public byte PathShearX;
        public byte PathShearY;
        public sbyte PathSkew;
        public ushort ProfileBegin;
        public ushort ProfileEnd;
        public LLVector3 Scale;
        public byte PathCurve;
        public byte ProfileCurve;
        public ushort ProfileHollow;
        public sbyte PathRadiusOffset;
        public byte PathRevolutions;
        public sbyte PathTaperX;
        public sbyte PathTaperY;
        public sbyte PathTwist;
        public sbyte PathTwistBegin;
        public byte[] TextureEntry; // a LL textureEntry in byte[] format
        public byte[] ExtraParams;

        public ShapeType PrimType
        {
            get
            {
                return this.type;
            }
        }

        public LLVector3 PrimScale
        {
            get
            {
                return this.Scale;
            }
        }

        public PrimitiveBaseShape()
        {
            ExtraParams = new byte[1];
        }

        //void returns need to change of course
        public virtual void GetMesh()
        {

        }

        public PrimitiveBaseShape Copy()
        {
            return (PrimitiveBaseShape)this.MemberwiseClone();
        }
    }

    public class BoxShape : PrimitiveBaseShape
    {
        public BoxShape()
        {
            type = ShapeType.Box;
            ExtraParams = new byte[1];
        }

        public static BoxShape Default
        {
            get
            {
                BoxShape primShape = new BoxShape();

                primShape.Scale = new LLVector3(0.5f, 0.5f, 0.5f);
                primShape.PCode = 9;
                primShape.PathBegin = 0;
                primShape.PathEnd = 0;
                primShape.PathScaleX = 0;
                primShape.PathScaleY = 0;
                primShape.PathShearX = 0;
                primShape.PathShearY = 0;
                primShape.PathSkew = 0;
                primShape.ProfileBegin = 0;
                primShape.ProfileEnd = 0;
                primShape.PathCurve = 16;
                primShape.ProfileCurve = 1;
                primShape.ProfileHollow = 0;
                primShape.PathRadiusOffset = 0;
                primShape.PathRevolutions = 0;
                primShape.PathTaperX = 0;
                primShape.PathTaperY = 0;
                primShape.PathTwist = 0;
                primShape.PathTwistBegin = 0;
                LLObject.TextureEntry ntex = new LLObject.TextureEntry(new LLUUID("00000000-0000-0000-9999-000000000005"));
                primShape.TextureEntry = ntex.ToBytes();
                primShape.ExtraParams = new byte[1];

                return primShape;
            }
        }
    }

    public class SphereShape : PrimitiveBaseShape
    {
        public SphereShape()
        {
            type = ShapeType.Sphere;
            ExtraParams = new byte[1];
        }
    }
}
