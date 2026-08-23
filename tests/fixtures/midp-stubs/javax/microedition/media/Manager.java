package javax.microedition.media;

public final class Manager {
    public static final String TONE_DEVICE_LOCATOR = "device://tone";
    private Manager() {}
    public static void playTone(int note, int duration, int volume) throws MediaException {}
    public static Player createPlayer(String locator) throws Exception { return null; }
    public static Player createPlayer(java.io.InputStream stream, String type) throws Exception { return null; }
}
