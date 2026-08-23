"""Generates the minimal MIDP API stubs used to compile demo and test MIDlets.

The emulator implements the real MIDP class library in C#, so nothing here is ever executed.
These are signature-only sources that let `javac` type-check a MIDlet without a copy of the
long-discontinued Sun Wireless Toolkit, and they are excluded from the JAR that gets built.
"""

import io
import os

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "tests", "fixtures", "midp-stubs")

STUBS = {}

STUBS["javax/microedition/midlet/MIDlet.java"] = """package javax.microedition.midlet;

public abstract class MIDlet {
    protected MIDlet() {}
    protected abstract void startApp();
    protected abstract void pauseApp();
    protected abstract void destroyApp(boolean unconditional);
    public final void notifyDestroyed() {}
    public final void notifyPaused() {}
    public final void resumeRequest() {}
    public final String getAppProperty(String key) { return null; }
    public final boolean platformRequest(String url) { return false; }
}
"""

STUBS["javax/microedition/lcdui/Displayable.java"] = """package javax.microedition.lcdui;

public abstract class Displayable {
    Displayable() {}
    public int getWidth() { return 0; }
    public int getHeight() { return 0; }
    public boolean isShown() { return false; }
    public String getTitle() { return null; }
    public void setTitle(String title) {}
    public void addCommand(Command command) {}
    public void removeCommand(Command command) {}
    public void setCommandListener(CommandListener listener) {}
    public void setTicker(Ticker ticker) {}
}
"""

STUBS["javax/microedition/lcdui/Screen.java"] = """package javax.microedition.lcdui;

public abstract class Screen extends Displayable {
    Screen() {}
}
"""

STUBS["javax/microedition/lcdui/Ticker.java"] = """package javax.microedition.lcdui;

public class Ticker {
    public Ticker(String text) {}
    public String getString() { return null; }
    public void setString(String text) {}
}
"""

STUBS["javax/microedition/lcdui/Display.java"] = """package javax.microedition.lcdui;

import javax.microedition.midlet.MIDlet;

public class Display {
    Display() {}
    public static Display getDisplay(MIDlet midlet) { return null; }
    public Displayable getCurrent() { return null; }
    public void setCurrent(Displayable next) {}
    public void setCurrent(Alert alert, Displayable next) {}
    public boolean isColor() { return true; }
    public int numColors() { return 0; }
    public int numAlphaLevels() { return 0; }
    public void callSerially(Runnable runnable) {}
    public boolean vibrate(int milliseconds) { return false; }
    public boolean flashBacklight(int milliseconds) { return false; }
}
"""

STUBS["javax/microedition/lcdui/Command.java"] = """package javax.microedition.lcdui;

public class Command {
    public static final int SCREEN = 1;
    public static final int BACK = 2;
    public static final int CANCEL = 3;
    public static final int OK = 4;
    public static final int HELP = 5;
    public static final int STOP = 6;
    public static final int EXIT = 7;
    public static final int ITEM = 8;

    public Command(String label, int commandType, int priority) {}
    public Command(String shortLabel, String longLabel, int commandType, int priority) {}
    public String getLabel() { return null; }
    public int getCommandType() { return 0; }
    public int getPriority() { return 0; }
}
"""

STUBS["javax/microedition/lcdui/CommandListener.java"] = """package javax.microedition.lcdui;

public interface CommandListener {
    void commandAction(Command command, Displayable displayable);
}
"""

STUBS["javax/microedition/lcdui/Font.java"] = """package javax.microedition.lcdui;

public final class Font {
    public static final int FACE_SYSTEM = 0;
    public static final int FACE_MONOSPACE = 32;
    public static final int FACE_PROPORTIONAL = 64;
    public static final int STYLE_PLAIN = 0;
    public static final int STYLE_BOLD = 1;
    public static final int STYLE_ITALIC = 2;
    public static final int STYLE_UNDERLINED = 4;
    public static final int SIZE_SMALL = 8;
    public static final int SIZE_MEDIUM = 0;
    public static final int SIZE_LARGE = 16;

    Font() {}
    public static Font getDefaultFont() { return null; }
    public static Font getFont(int face, int style, int size) { return null; }
    public int getHeight() { return 0; }
    public int getBaselinePosition() { return 0; }
    public int stringWidth(String text) { return 0; }
    public int charWidth(char character) { return 0; }
    public int substringWidth(String text, int offset, int length) { return 0; }
}
"""

STUBS["javax/microedition/lcdui/Image.java"] = """package javax.microedition.lcdui;

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
"""

STUBS["javax/microedition/lcdui/Graphics.java"] = """package javax.microedition.lcdui;

public class Graphics {
    public static final int HCENTER = 1;
    public static final int VCENTER = 2;
    public static final int LEFT = 4;
    public static final int RIGHT = 8;
    public static final int TOP = 16;
    public static final int BOTTOM = 32;
    public static final int BASELINE = 64;
    public static final int SOLID = 0;
    public static final int DOTTED = 1;

    Graphics() {}
    public void setColor(int rgb) {}
    public void setColor(int red, int green, int blue) {}
    public int getColor() { return 0; }
    public void setGrayScale(int value) {}
    public void translate(int x, int y) {}
    public int getTranslateX() { return 0; }
    public int getTranslateY() { return 0; }
    public void setClip(int x, int y, int width, int height) {}
    public void clipRect(int x, int y, int width, int height) {}
    public int getClipX() { return 0; }
    public int getClipY() { return 0; }
    public int getClipWidth() { return 0; }
    public int getClipHeight() { return 0; }
    public void setStrokeStyle(int style) {}
    public void drawLine(int x1, int y1, int x2, int y2) {}
    public void fillRect(int x, int y, int width, int height) {}
    public void drawRect(int x, int y, int width, int height) {}
    public void fillRoundRect(int x, int y, int width, int height, int arcWidth, int arcHeight) {}
    public void drawRoundRect(int x, int y, int width, int height, int arcWidth, int arcHeight) {}
    public void fillArc(int x, int y, int width, int height, int startAngle, int arcAngle) {}
    public void drawArc(int x, int y, int width, int height, int startAngle, int arcAngle) {}
    public void fillTriangle(int x1, int y1, int x2, int y2, int x3, int y3) {}
    public void setFont(Font font) {}
    public Font getFont() { return null; }
    public void drawString(String text, int x, int y, int anchor) {}
    public void drawSubstring(String text, int offset, int length, int x, int y, int anchor) {}
    public void drawChar(char character, int x, int y, int anchor) {}
    public void drawImage(Image image, int x, int y, int anchor) {}
    public void drawRegion(Image src, int sx, int sy, int sw, int sh, int transform, int dx, int dy, int anchor) {}
    public void copyArea(int sx, int sy, int width, int height, int dx, int dy, int anchor) {}
    public void drawRGB(int[] rgb, int offset, int scanLength, int x, int y, int width, int height, boolean alpha) {}
}
"""

STUBS["javax/microedition/lcdui/Canvas.java"] = """package javax.microedition.lcdui;

public abstract class Canvas extends Displayable {
    public static final int KEY_NUM0 = 48;
    public static final int KEY_NUM1 = 49;
    public static final int KEY_NUM2 = 50;
    public static final int KEY_NUM3 = 51;
    public static final int KEY_NUM4 = 52;
    public static final int KEY_NUM5 = 53;
    public static final int KEY_NUM6 = 54;
    public static final int KEY_NUM7 = 55;
    public static final int KEY_NUM8 = 56;
    public static final int KEY_NUM9 = 57;
    public static final int KEY_STAR = 42;
    public static final int KEY_POUND = 35;
    public static final int UP = 1;
    public static final int LEFT = 2;
    public static final int RIGHT = 5;
    public static final int DOWN = 6;
    public static final int FIRE = 8;
    public static final int GAME_A = 9;
    public static final int GAME_B = 10;
    public static final int GAME_C = 11;
    public static final int GAME_D = 12;

    protected Canvas() {}
    protected abstract void paint(Graphics graphics);
    protected void keyPressed(int keyCode) {}
    protected void keyReleased(int keyCode) {}
    protected void keyRepeated(int keyCode) {}
    protected void pointerPressed(int x, int y) {}
    protected void pointerReleased(int x, int y) {}
    protected void pointerDragged(int x, int y) {}
    protected void showNotify() {}
    protected void hideNotify() {}
    protected void sizeChanged(int width, int height) {}
    public int getGameAction(int keyCode) { return 0; }
    public int getKeyCode(int gameAction) { return 0; }
    public String getKeyName(int keyCode) { return null; }
    public boolean hasPointerEvents() { return false; }
    public boolean hasRepeatEvents() { return false; }
    public boolean isDoubleBuffered() { return true; }
    public void setFullScreenMode(boolean mode) {}
    public final void repaint() {}
    public final void repaint(int x, int y, int width, int height) {}
    public final void serviceRepaints() {}
}
"""

STUBS["javax/microedition/lcdui/Alert.java"] = """package javax.microedition.lcdui;

public class Alert extends Screen {
    public static final int FOREVER = -2;
    public Alert(String title) {}
    public Alert(String title, String text, Image image, AlertType type) {}
    public void setString(String text) {}
    public String getString() { return null; }
    public void setTimeout(int timeout) {}
    public int getTimeout() { return 0; }
}
"""

STUBS["javax/microedition/lcdui/AlertType.java"] = """package javax.microedition.lcdui;

public class AlertType {
    public static final AlertType INFO = null;
    public static final AlertType WARNING = null;
    public static final AlertType ERROR = null;
    public static final AlertType ALARM = null;
    public static final AlertType CONFIRMATION = null;
    protected AlertType() {}
    public boolean playSound(Display display) { return false; }
}
"""

STUBS["javax/microedition/lcdui/List.java"] = """package javax.microedition.lcdui;

public class List extends Screen implements Choice {
    public static final Command SELECT_COMMAND = null;
    public List(String title, int listType) {}
    public List(String title, int listType, String[] items, Image[] icons) {}
    public int append(String text, Image icon) { return 0; }
    public void insert(int index, String text, Image icon) {}
    public void set(int index, String text, Image icon) {}
    public void delete(int index) {}
    public void deleteAll() {}
    public int size() { return 0; }
    public String getString(int index) { return null; }
    public Image getImage(int index) { return null; }
    public int getSelectedIndex() { return 0; }
    public void setSelectedIndex(int index, boolean selected) {}
    public boolean isSelected(int index) { return false; }
}
"""

STUBS["javax/microedition/lcdui/Choice.java"] = """package javax.microedition.lcdui;

public interface Choice {
    int EXCLUSIVE = 1;
    int MULTIPLE = 2;
    int IMPLICIT = 3;
    int POPUP = 4;
}
"""

STUBS["javax/microedition/lcdui/game/GameCanvas.java"] = """package javax.microedition.lcdui.game;

import javax.microedition.lcdui.Canvas;
import javax.microedition.lcdui.Graphics;

public abstract class GameCanvas extends Canvas {
    public static final int UP_PRESSED = 0x0002;
    public static final int LEFT_PRESSED = 0x0004;
    public static final int RIGHT_PRESSED = 0x0020;
    public static final int DOWN_PRESSED = 0x0040;
    public static final int FIRE_PRESSED = 0x0100;
    public static final int GAME_A_PRESSED = 0x0200;
    public static final int GAME_B_PRESSED = 0x0400;
    public static final int GAME_C_PRESSED = 0x0800;
    public static final int GAME_D_PRESSED = 0x1000;

    protected GameCanvas(boolean suppressKeyEvents) {}
    protected Graphics getGraphics() { return null; }
    public void flushGraphics() {}
    public void flushGraphics(int x, int y, int width, int height) {}
    public int getKeyStates() { return 0; }
}
"""

STUBS["javax/microedition/lcdui/game/Layer.java"] = """package javax.microedition.lcdui.game;

import javax.microedition.lcdui.Graphics;

public abstract class Layer {
    Layer() {}
    public void setPosition(int x, int y) {}
    public void move(int dx, int dy) {}
    public final int getX() { return 0; }
    public final int getY() { return 0; }
    public final int getWidth() { return 0; }
    public final int getHeight() { return 0; }
    public void setVisible(boolean visible) {}
    public final boolean isVisible() { return false; }
    public abstract void paint(Graphics graphics);
}
"""

STUBS["javax/microedition/lcdui/game/Sprite.java"] = """package javax.microedition.lcdui.game;

import javax.microedition.lcdui.Graphics;
import javax.microedition.lcdui.Image;

public class Sprite extends Layer {
    public static final int TRANS_NONE = 0;
    public static final int TRANS_MIRROR_ROT180 = 1;
    public static final int TRANS_MIRROR = 2;
    public static final int TRANS_ROT180 = 3;
    public static final int TRANS_MIRROR_ROT270 = 4;
    public static final int TRANS_ROT90 = 5;
    public static final int TRANS_ROT270 = 6;
    public static final int TRANS_MIRROR_ROT90 = 7;

    public Sprite(Image image) {}
    public Sprite(Image image, int frameWidth, int frameHeight) {}
    public void setFrame(int sequenceIndex) {}
    public int getFrame() { return 0; }
    public void nextFrame() {}
    public void prevFrame() {}
    public int getFrameSequenceLength() { return 0; }
    public void setFrameSequence(int[] sequence) {}
    public void setTransform(int transform) {}
    public void defineReferencePixel(int x, int y) {}
    public void setRefPixelPosition(int x, int y) {}
    public void defineCollisionRectangle(int x, int y, int width, int height) {}
    public boolean collidesWith(Sprite other, boolean pixelLevel) { return false; }
    public boolean collidesWith(TiledLayer layer, boolean pixelLevel) { return false; }
    public boolean collidesWith(Image image, int x, int y, boolean pixelLevel) { return false; }
    public void paint(Graphics graphics) {}
}
"""

STUBS["javax/microedition/lcdui/game/TiledLayer.java"] = """package javax.microedition.lcdui.game;

import javax.microedition.lcdui.Graphics;
import javax.microedition.lcdui.Image;

public class TiledLayer extends Layer {
    public TiledLayer(int columns, int rows, Image image, int tileWidth, int tileHeight) {}
    public int getColumns() { return 0; }
    public int getRows() { return 0; }
    public int getCellWidth() { return 0; }
    public int getCellHeight() { return 0; }
    public void setCell(int column, int row, int tileIndex) {}
    public int getCell(int column, int row) { return 0; }
    public void fillCells(int column, int row, int columns, int rows, int tileIndex) {}
    public int createAnimatedTile(int staticTileIndex) { return 0; }
    public void setAnimatedTile(int animatedTileIndex, int staticTileIndex) {}
    public void paint(Graphics graphics) {}
}
"""

STUBS["javax/microedition/lcdui/game/LayerManager.java"] = """package javax.microedition.lcdui.game;

import javax.microedition.lcdui.Graphics;

public class LayerManager {
    public LayerManager() {}
    public void append(Layer layer) {}
    public void insert(Layer layer, int index) {}
    public void remove(Layer layer) {}
    public Layer getLayerAt(int index) { return null; }
    public int getSize() { return 0; }
    public void setViewWindow(int x, int y, int width, int height) {}
    public void paint(Graphics graphics, int x, int y) {}
}
"""

STUBS["javax/microedition/rms/RecordStore.java"] = """package javax.microedition.rms;

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
"""

STUBS["javax/microedition/media/Manager.java"] = """package javax.microedition.media;

public final class Manager {
    public static final String TONE_DEVICE_LOCATOR = "device://tone";
    private Manager() {}
    public static void playTone(int note, int duration, int volume) throws MediaException {}
    public static Player createPlayer(String locator) throws Exception { return null; }
    public static Player createPlayer(java.io.InputStream stream, String type) throws Exception { return null; }
}
"""

STUBS["javax/microedition/media/Player.java"] = """package javax.microedition.media;

public interface Player {
    int UNREALIZED = 100;
    int REALIZED = 200;
    int PREFETCHED = 300;
    int STARTED = 400;
    int CLOSED = 0;

    void realize() throws MediaException;
    void prefetch() throws MediaException;
    void start() throws MediaException;
    void stop() throws MediaException;
    void close();
    int getState();
    void setLoopCount(int count);
}
"""

STUBS["javax/microedition/media/MediaException.java"] = """package javax.microedition.media;

public class MediaException extends Exception {
    public MediaException() {}
    public MediaException(String message) { super(message); }
}
"""


def main():
    written = 0
    for relative, body in STUBS.items():
        path = os.path.normpath(os.path.join(ROOT, relative))
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with io.open(path, "w", encoding="utf-8", newline="\n") as handle:
            handle.write(body)
        written += 1

    print("wrote {0} stub sources under {1}".format(written, os.path.normpath(ROOT)))


if __name__ == "__main__":
    main()
