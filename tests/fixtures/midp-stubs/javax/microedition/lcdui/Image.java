package javax.microedition.lcdui;

public class Image {
    Image() {}
    public static Image createImage(int width, int height) { return null; }
    public static Image createImage(String name) throws java.io.IOException { return null; }
    public static Image createImage(byte[] data, int offset, int length) { return null; }
    public static Image createImage(Image source) { return null; }
    public static Image createRGBImage(int[] rgb, int width, int height, boolean processAlpha) { return null; }
    public Graphics getGraphics() { return null; }
    public int getWidth() { return 0; }
    public int getHeight() { return 0; }
    public boolean isMutable() { return false; }
    public void getRGB(int[] rgb, int offset, int scanLength, int x, int y, int width, int height) {}
}
