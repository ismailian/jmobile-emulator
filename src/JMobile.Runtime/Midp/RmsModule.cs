using System.Buffers.Binary;
using JMobile.Core;
using JMobile.Core.Execution;
using JMobile.Core.Model;
using JMobile.Core.Native;
using JMobile.Runtime.Host;
using JMobile.Runtime.Util;

namespace JMobile.Runtime.Midp;

/// <summary>
/// The record management system, which is where every J2ME game keeps its save data.
/// </summary>
/// <remarks>
/// A record store is a flat map from integer id to byte array, with ids that are never reused.
/// The on-disk format here is deliberately simple and versioned, because the one thing a player
/// will not forgive is losing a save: a store is written to a temporary file and moved into
/// place, and a store that fails to parse is reported rather than silently replaced.
/// </remarks>
public sealed class RmsModule : IRuntimeModule
{
    public string Name => "javax.microedition.rms";

    /// <summary>Magic and version prefix of the persisted format.</summary>
    private const uint StoreMagic = 0x4A524D53; // "JRMS"

    private const int StoreVersion = 1;

    public void Register(RuntimeRegistry registry)
    {
        RegisterExceptions(registry);
        RegisterInterfaces(registry);
        RegisterRecordStore(registry);
        RegisterEnumeration(registry);
    }

    private static void RegisterExceptions(RuntimeRegistry registry)
    {
        registry.DefineThrowable("javax/microedition/rms/RecordStoreException", WellKnown.Exception);
        registry.DefineThrowable("javax/microedition/rms/RecordStoreNotOpenException",
            "javax/microedition/rms/RecordStoreException");
        registry.DefineThrowable("javax/microedition/rms/RecordStoreFullException",
            "javax/microedition/rms/RecordStoreException");
        registry.DefineThrowable("javax/microedition/rms/RecordStoreNotFoundException",
            "javax/microedition/rms/RecordStoreException");
        registry.DefineThrowable("javax/microedition/rms/InvalidRecordIDException",
            "javax/microedition/rms/RecordStoreException");
    }

    private static void RegisterInterfaces(RuntimeRegistry registry)
    {
        registry.DefineInterface("javax/microedition/rms/RecordFilter", builder =>
            builder.InterfaceMethod("matches", "([B)Z"));

        registry.DefineInterface("javax/microedition/rms/RecordComparator", builder => builder
            .Constant("EQUIVALENT", 0)
            .Constant("FOLLOWS", 1)
            .Constant("PRECEDES", -1)
            .InterfaceMethod("compare", "([B[B)I"));

        registry.DefineInterface("javax/microedition/rms/RecordListener", builder => builder
            .InterfaceMethod("recordAdded", "(Ljavax/microedition/rms/RecordStore;I)V")
            .InterfaceMethod("recordChanged", "(Ljavax/microedition/rms/RecordStore;I)V")
            .InterfaceMethod("recordDeleted", "(Ljavax/microedition/rms/RecordStore;I)V"));
    }

    // ---------------------------------------------------------------------------------
    // RecordStore
    // ---------------------------------------------------------------------------------

    private static void RegisterRecordStore(RuntimeRegistry registry)
    {
        registry.Define("javax/microedition/rms/RecordStore", builder => builder
            .AllocatesWith(static type => new JavaRecordStore(type, type.InstanceFieldCount))
            .Constant("AUTHMODE_PRIVATE", 0)
            .Constant("AUTHMODE_ANY", 1)

            .StaticMethod("openRecordStore",
                "(Ljava/lang/String;Z)Ljavax/microedition/rms/RecordStore;",
                static (thread, args) => JavaValue.Ref(Open(thread,
                    RuntimeSupport.RequireString(thread, args.String(0), "recordStoreName"),
                    args.Bool(1))))
            .StaticMethod("openRecordStore",
                "(Ljava/lang/String;ZIZ)Ljavax/microedition/rms/RecordStore;",
                static (thread, args) => JavaValue.Ref(Open(thread,
                    RuntimeSupport.RequireString(thread, args.String(0), "recordStoreName"),
                    args.Bool(1))))
            .StaticMethod("deleteRecordStore", "(Ljava/lang/String;)V", static (thread, args) =>
            {
                string name = RuntimeSupport.RequireString(thread, args.String(0), "recordStoreName");
                IStorageHost storage = RequireStorage(thread);
                if (!storage.TryRead(name, out _))
                {
                    throw thread.Vm.NewException(
                        thread, "javax/microedition/rms/RecordStoreNotFoundException", name);
                }

                storage.Delete(name);
                MidpRuntime.Of(thread.Vm).OpenRecordStores.Remove(name);
                return JavaValue.Void;
            })
            .StaticMethod("listRecordStores", "()[Ljava/lang/String;", static (thread, _) =>
            {
                IReadOnlyList<string> names = RequireStorage(thread).List();
                // The specification says an empty list is reported as null, not an empty array.
                return names.Count == 0
                    ? JavaValue.Null
                    : JavaValue.Ref(thread.Vm.NewStringArray(names));
            })

            .Method("getName", "()Ljava/lang/String;",
                static (thread, args) => RuntimeSupport.Str(thread, args.Self<JavaRecordStore>().StoreName))
            .Method("getVersion", "()I", static (thread, args) =>
                JavaValue.Int(RequireOpen(thread, args.Self<JavaRecordStore>()).Version))
            .Method("getLastModified", "()J", static (thread, args) =>
                JavaValue.Long(RequireOpen(thread, args.Self<JavaRecordStore>()).LastModified))
            .Method("getNumRecords", "()I", static (thread, args) =>
                JavaValue.Int(RequireOpen(thread, args.Self<JavaRecordStore>()).Records.Count))
            .Method("getNextRecordID", "()I", static (thread, args) =>
                JavaValue.Int(RequireOpen(thread, args.Self<JavaRecordStore>()).NextRecordId))
            .Method("getSize", "()I", static (thread, args) =>
                JavaValue.Int(RequireOpen(thread, args.Self<JavaRecordStore>())
                    .Records.Values.Sum(record => record.Length)))
            .Method("getSizeAvailable", "()I", static (_, _) => JavaValue.Int(1 << 20))

            .Method("addRecord", "([BII)I", static (thread, args) =>
            {
                JavaRecordStore store = RequireOpen(thread, args.Self<JavaRecordStore>());
                byte[] data = ExtractRegion(thread, args.Object<JavaArray<sbyte>>(0), args.Int(1), args.Int(2));

                int id = store.NextRecordId++;
                store.Records[id] = data;
                Touch(thread, store);
                return JavaValue.Int(id);
            })
            .Method("getRecord", "(I)[B", static (thread, args) =>
            {
                JavaRecordStore store = RequireOpen(thread, args.Self<JavaRecordStore>());
                byte[] record = RequireRecord(thread, store, args.Int(0));
                return JavaValue.Ref(thread.Vm.NewByteArray(record));
            })
            .Method("getRecord", "(I[BI)I", static (thread, args) =>
            {
                JavaRecordStore store = RequireOpen(thread, args.Self<JavaRecordStore>());
                byte[] record = RequireRecord(thread, store, args.Int(0));

                var buffer = args.Object<JavaArray<sbyte>>(1)
                             ?? throw thread.Vm.NewNullPointerException(thread, "buffer");
                int offset = args.Int(2);
                if (offset < 0 || offset + record.Length > buffer.Length)
                    throw thread.Vm.NewArrayIndexOutOfBoundsException(thread, offset, buffer.Length);

                RuntimeSupport.FromHostBytes(record, buffer, offset);
                return JavaValue.Int(record.Length);
            })
            .Method("getRecordSize", "(I)I", static (thread, args) =>
            {
                JavaRecordStore store = RequireOpen(thread, args.Self<JavaRecordStore>());
                return JavaValue.Int(RequireRecord(thread, store, args.Int(0)).Length);
            })
            .Method("setRecord", "(I[BII)V", static (thread, args) =>
            {
                JavaRecordStore store = RequireOpen(thread, args.Self<JavaRecordStore>());
                int id = args.Int(0);
                RequireRecord(thread, store, id);

                store.Records[id] = ExtractRegion(thread, args.Object<JavaArray<sbyte>>(1),
                    args.Int(2), args.Int(3));
                Touch(thread, store);
                return JavaValue.Void;
            })
            .Method("deleteRecord", "(I)V", static (thread, args) =>
            {
                JavaRecordStore store = RequireOpen(thread, args.Self<JavaRecordStore>());
                int id = args.Int(0);
                RequireRecord(thread, store, id);

                store.Records.Remove(id);
                Touch(thread, store);
                return JavaValue.Void;
            })
            .Method("closeRecordStore", "()V", static (thread, args) =>
            {
                JavaRecordStore store = RequireOpen(thread, args.Self<JavaRecordStore>());
                store.OpenCount--;
                if (store.OpenCount > 0) return JavaValue.Void;

                Persist(thread, store);
                store.IsOpen = false;
                MidpRuntime.Of(thread.Vm).OpenRecordStores.Remove(store.StoreName);
                return JavaValue.Void;
            })
            .Method("enumerateRecords",
                "(Ljavax/microedition/rms/RecordFilter;Ljavax/microedition/rms/RecordComparator;Z)Ljavax/microedition/rms/RecordEnumeration;",
                static (thread, args) =>
                {
                    JavaRecordStore store = RequireOpen(thread, args.Self<JavaRecordStore>());
                    return JavaValue.Ref(BuildEnumeration(thread, store, args.Object(0), args.Object(1)));
                })
            .NoOpMethod("addRecordListener", "(Ljavax/microedition/rms/RecordListener;)V")
            .NoOpMethod("removeRecordListener", "(Ljavax/microedition/rms/RecordListener;)V"));
    }

    private static IStorageHost RequireStorage(JavaThread thread) =>
        MidpRuntime.Of(thread.Vm).Storage
        ?? throw thread.Vm.NewException(thread, "javax/microedition/rms/RecordStoreException",
            "no storage is available");

    private static JavaObject Open(JavaThread thread, string name, bool createIfNecessary)
    {
        Dictionary<string, JavaRecordStore> openStores = MidpRuntime.Of(thread.Vm).OpenRecordStores;
        if (openStores.TryGetValue(name, out JavaRecordStore? already))
        {
            already.OpenCount++;
            return already;
        }

        IStorageHost storage = RequireStorage(thread);
        bool exists = storage.TryRead(name, out byte[] data);

        if (!exists && !createIfNecessary)
        {
            throw thread.Vm.NewException(
                thread, "javax/microedition/rms/RecordStoreNotFoundException", name);
        }

        JavaClass type = thread.Vm.LoadClass("javax/microedition/rms/RecordStore");
        var store = (JavaRecordStore)thread.Vm.NewInstance(type);
        store.StoreName = name;
        store.IsOpen = true;
        store.OpenCount = 1;
        store.LastModified = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (exists) Load(thread, store, data);

        openStores[name] = store;
        return store;
    }

    private static JavaRecordStore RequireOpen(JavaThread thread, JavaRecordStore store)
    {
        if (!store.IsOpen)
        {
            throw thread.Vm.NewException(
                thread, "javax/microedition/rms/RecordStoreNotOpenException", store.StoreName);
        }
        return store;
    }

    private static byte[] RequireRecord(JavaThread thread, JavaRecordStore store, int id)
    {
        if (!store.Records.TryGetValue(id, out byte[]? record))
        {
            throw thread.Vm.NewException(
                thread, "javax/microedition/rms/InvalidRecordIDException", id.ToString());
        }
        return record;
    }

    private static byte[] ExtractRegion(JavaThread thread, JavaArray<sbyte>? source, int offset, int length)
    {
        // A null array with a zero length is how a game writes an empty record.
        if (source is null) return [];

        RuntimeSupport.CheckRegion(thread, offset, length, source.Length);
        return RuntimeSupport.ToHostBytes(source, offset, length);
    }

    private static void Touch(JavaThread thread, JavaRecordStore store)
    {
        store.Version++;
        store.LastModified = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Written through on every change. Games are killed rather than closed far more often
        // than they exit cleanly, and an unwritten save is a lost save.
        Persist(thread, store);
    }

    private static void Persist(JavaThread thread, JavaRecordStore store)
    {
        if (MidpRuntime.Of(thread.Vm).Storage is not { } storage) return;

        using var buffer = new MemoryStream();
        Span<byte> header = stackalloc byte[16];
        BinaryPrimitives.WriteUInt32BigEndian(header, StoreMagic);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], StoreVersion);
        BinaryPrimitives.WriteInt32BigEndian(header[8..], store.NextRecordId);
        BinaryPrimitives.WriteInt32BigEndian(header[12..], store.Records.Count);
        buffer.Write(header);

        Span<byte> entry = stackalloc byte[8];
        foreach (KeyValuePair<int, byte[]> record in store.Records)
        {
            BinaryPrimitives.WriteInt32BigEndian(entry, record.Key);
            BinaryPrimitives.WriteInt32BigEndian(entry[4..], record.Value.Length);
            buffer.Write(entry);
            buffer.Write(record.Value);
        }

        storage.Write(store.StoreName, buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
    }

    private static void Load(JavaThread thread, JavaRecordStore store, byte[] data)
    {
        if (data.Length < 16) return;

        var reader = new ReadOnlySpan<byte>(data);
        if (BinaryPrimitives.ReadUInt32BigEndian(reader) != StoreMagic)
        {
            throw thread.Vm.NewException(thread, "javax/microedition/rms/RecordStoreException",
                $"record store '{store.StoreName}' is not in a format this emulator wrote");
        }

        store.NextRecordId = BinaryPrimitives.ReadInt32BigEndian(reader[8..]);
        int count = BinaryPrimitives.ReadInt32BigEndian(reader[12..]);

        int offset = 16;
        for (int i = 0; i < count && offset + 8 <= data.Length; i++)
        {
            int id = BinaryPrimitives.ReadInt32BigEndian(reader[offset..]);
            int length = BinaryPrimitives.ReadInt32BigEndian(reader[(offset + 4)..]);
            offset += 8;

            if (length < 0 || offset + length > data.Length) break;
            store.Records[id] = reader.Slice(offset, length).ToArray();
            offset += length;
        }

        if (store.NextRecordId <= 0) store.NextRecordId = 1;
    }

    // ---------------------------------------------------------------------------------
    // RecordEnumeration
    // ---------------------------------------------------------------------------------

    private static void RegisterEnumeration(RuntimeRegistry registry)
    {
        registry.DefineInterface("javax/microedition/rms/RecordEnumeration", builder => builder
            .InterfaceMethod("numRecords", "()I")
            .InterfaceMethod("nextRecord", "()[B")
            .InterfaceMethod("nextRecordId", "()I")
            .InterfaceMethod("hasNextElement", "()Z")
            .InterfaceMethod("reset", "()V")
            .InterfaceMethod("destroy", "()V"));

        registry.Define("javax/microedition/rms/RecordEnumerationImpl", builder => builder
            .Implements("javax/microedition/rms/RecordEnumeration")
            .AllocatesWith(static type => new JavaRecordEnumeration(type, type.InstanceFieldCount))
            .Method("numRecords", "()I",
                static (_, args) => JavaValue.Int(args.Self<JavaRecordEnumeration>().Ids.Length))
            .Method("hasNextElement", "()Z", static (_, args) =>
            {
                var self = args.Self<JavaRecordEnumeration>();
                return JavaValue.Bool(self.Cursor < self.Ids.Length);
            })
            .Method("hasPreviousElement", "()Z",
                static (_, args) => JavaValue.Bool(args.Self<JavaRecordEnumeration>().Cursor > 0))
            .Method("nextRecordId", "()I", static (thread, args) =>
            {
                var self = args.Self<JavaRecordEnumeration>();
                if (self.Cursor >= self.Ids.Length) throw thread.Vm.NewNoSuchElementException(thread);
                return JavaValue.Int(self.Ids[self.Cursor++]);
            })
            .Method("nextRecord", "()[B", static (thread, args) =>
            {
                var self = args.Self<JavaRecordEnumeration>();
                if (self.Cursor >= self.Ids.Length) throw thread.Vm.NewNoSuchElementException(thread);

                int id = self.Ids[self.Cursor++];
                return JavaValue.Ref(thread.Vm.NewByteArray(self.Store.Records[id]));
            })
            .Method("previousRecord", "()[B", static (thread, args) =>
            {
                var self = args.Self<JavaRecordEnumeration>();
                if (self.Cursor <= 0) throw thread.Vm.NewNoSuchElementException(thread);

                int id = self.Ids[--self.Cursor];
                return JavaValue.Ref(thread.Vm.NewByteArray(self.Store.Records[id]));
            })
            .Method("reset", "()V", static (_, args) =>
            {
                args.Self<JavaRecordEnumeration>().Cursor = 0;
                return JavaValue.Void;
            })
            .NoOpMethod("destroy", "()V")
            .NoOpMethod("keepUpdated", "(Z)V")
            .NoOpMethod("rebuild", "()V"));
    }

    /// <summary>
    /// Builds a record enumeration, applying the game's filter and comparator eagerly.
    /// </summary>
    /// <remarks>
    /// Both callbacks are guest bytecode, so running them once at construction rather than
    /// during iteration keeps the enumeration cheap to walk and, more importantly, keeps a
    /// comparator that throws from corrupting iteration halfway through.
    /// </remarks>
    private static JavaObject BuildEnumeration(
        JavaThread thread,
        JavaRecordStore store,
        JavaObject? filter,
        JavaObject? comparator)
    {
        List<int> ids = [];

        foreach (KeyValuePair<int, byte[]> record in store.Records)
        {
            if (filter is not null)
            {
                JavaValue matches = thread.Vm.CallVirtual(thread, filter, "matches", "([B)Z",
                    JavaValue.Ref(thread.Vm.NewByteArray(record.Value)));
                if (!matches.AsBool) continue;
            }

            ids.Add(record.Key);
        }

        if (comparator is not null)
        {
            ids.Sort((left, right) =>
                thread.Vm.CallVirtual(thread, comparator, "compare", "([B[B)I",
                    JavaValue.Ref(thread.Vm.NewByteArray(store.Records[left])),
                    JavaValue.Ref(thread.Vm.NewByteArray(store.Records[right]))).AsInt);
        }
        else
        {
            ids.Sort();
        }

        JavaClass type = thread.Vm.LoadClass("javax/microedition/rms/RecordEnumerationImpl");
        var enumeration = (JavaRecordEnumeration)thread.Vm.NewInstance(type);
        enumeration.Store = store;
        enumeration.Ids = ids.ToArray();
        return enumeration;
    }
}

/// <summary>Iteration state over a filtered, sorted view of a record store.</summary>
public sealed class JavaRecordEnumeration(JavaClass type, int fieldCount) : JavaObject(type, fieldCount)
{
    public JavaRecordStore Store { get; set; } = null!;

    public int[] Ids { get; set; } = [];

    public int Cursor { get; set; }
}
