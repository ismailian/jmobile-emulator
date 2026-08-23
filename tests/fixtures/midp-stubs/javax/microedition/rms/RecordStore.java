package javax.microedition.rms;

public class RecordStore {
    RecordStore() {}
    public static RecordStore openRecordStore(String name, boolean createIfNecessary) throws Exception { return null; }
    public static void deleteRecordStore(String name) throws Exception {}
    public static String[] listRecordStores() { return null; }
    public void closeRecordStore() throws Exception {}
    public String getName() throws Exception { return null; }
    public int getNumRecords() throws Exception { return 0; }
    public int getNextRecordID() throws Exception { return 0; }
    public int addRecord(byte[] data, int offset, int length) throws Exception { return 0; }
    public byte[] getRecord(int recordId) throws Exception { return null; }
    public int getRecord(int recordId, byte[] buffer, int offset) throws Exception { return 0; }
    public int getRecordSize(int recordId) throws Exception { return 0; }
    public void setRecord(int recordId, byte[] data, int offset, int length) throws Exception {}
    public void deleteRecord(int recordId) throws Exception {}
}
