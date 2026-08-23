package javax.microedition.lcdui;

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
