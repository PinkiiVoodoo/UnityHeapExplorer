using System.Collections.Generic;

namespace HeapExplorer
{
    public class ObjectProxy : IEqualityComparer<ObjectProxy>
    {
        public PackedMemorySnapshot snapshot;
        public RichNativeObject native;
        public RichManagedObject managed;
        public RichGCHandle gcHandle;
        public RichStaticField staticField;

        public System.Int64 id
        {
            get
            {
                if (native.isValid)
                    return (1 << 62) + native.packed.nativeObjectsArrayIndex;

                if (managed.isValid)
                    return (1 << 61) + managed.packed.managedObjectsArrayIndex;

                if (gcHandle.isValid)
                    return (1 << 60) + gcHandle.packed.gcHandlesArrayIndex;

                if (staticField.isValid)
                    return (1 << 59) + staticField.packed.staticFieldsArrayIndex;

                return 0;
            }
        }

        public ObjectProxy(PackedMemorySnapshot snp, PackedNativeUnityEngineObject packed)
        {
            snapshot = snp;
            native = new RichNativeObject(snp, packed.nativeObjectsArrayIndex);
        }

        public ObjectProxy(PackedMemorySnapshot snp, PackedManagedObject packed)
        {
            snapshot = snp;
            managed = new RichManagedObject(snp, packed.managedObjectsArrayIndex);
        }

        public ObjectProxy(PackedMemorySnapshot snp, PackedGCHandle packed)
        {
            snapshot = snp;
            gcHandle = new RichGCHandle(snp, packed.gcHandlesArrayIndex);
        }

        public ObjectProxy(PackedMemorySnapshot snp, PackedManagedStaticField packed)
        {
            snapshot = snp;
            staticField = new RichStaticField(snp, packed.staticFieldsArrayIndex);
        }

        public override string ToString()
        {
            if (native.isValid)
                return string.Format("Native, {0}", native);

            if (managed.isValid)
                return string.Format("Managed, {0}", managed);

            if (gcHandle.isValid)
                return string.Format("GCHandle, {0}", gcHandle);

            if (staticField.isValid)
                return string.Format("StaticField, {0}", staticField);

            return base.ToString();
        }

        public bool Equals(ObjectProxy x, ObjectProxy y)
        {
            return x?.id == y?.id;
        }

        public int GetHashCode(ObjectProxy obj)
        {
            return obj.id.GetHashCode();
        }
        
        public override bool Equals(object obj)
        {
            return obj is ObjectProxy other && this.id == other.id;
        }
        
        public override int GetHashCode()
        {
            return id.GetHashCode();
        }
    }
}
