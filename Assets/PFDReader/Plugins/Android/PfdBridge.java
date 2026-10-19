package org.pfd.reader.unity;

import android.app.Activity;
import android.content.Intent;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.pdf.PdfRenderer;
import android.net.Uri;
import android.os.Build;
import android.os.Environment;
import android.os.ParcelFileDescriptor;
import android.provider.Settings;
import java.io.File;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;

/** Kleine Java-Bruecke fuer Unity: PDF-Rendering, Bilddekodierung, Speicherberechtigung. */
public final class PfdBridge {
    private PfdBridge() {}

    // ------------------------------------------------------------ Speicher
    public static boolean hasAllFilesAccess() {
        if (Build.VERSION.SDK_INT >= 30) return Environment.isExternalStorageManager();
        return true;
    }

    public static void requestAllFilesAccess(Activity activity) {
        if (Build.VERSION.SDK_INT < 30) return;
        try {
            Intent i = new Intent(Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION);
            i.setData(Uri.parse("package:" + activity.getPackageName()));
            activity.startActivity(i);
        } catch (Exception e) {
            activity.startActivity(new Intent(Settings.ACTION_MANAGE_ALL_FILES_ACCESS_PERMISSION));
        }
    }

    public static String externalRoot() { return Environment.getExternalStorageDirectory().getAbsolutePath(); }

    // ------------------------------------------------------------ PDF
    /** Haelt einen geoeffneten PdfRenderer. Nicht threadsicher: Aufrufe pro Instanz serialisieren. */
    public static final class Pdf {
        private final ParcelFileDescriptor fd;
        private final PdfRenderer renderer;
        public Pdf(String path) throws Exception {
            fd = ParcelFileDescriptor.open(new File(path), ParcelFileDescriptor.MODE_READ_ONLY);
            renderer = new PdfRenderer(fd);
        }
        public synchronized int pageCount() { return renderer.getPageCount(); }
        /** Seitenverhaeltnis Breite/Hoehe. */
        public synchronized float aspect(int index) {
            PdfRenderer.Page p = renderer.openPage(index);
            try { return p.getWidth() / (float) p.getHeight(); } finally { p.close(); }
        }
        /**
         * Rendert die Seite (oder einen Ausschnitt bei zoom>1) auf weissem Grund in w*h RGBA-Bytes,
         * Zeile 0 = unten (Unity-Texturkonvention). panX/panY in 0..1 (Mittelpunkt des Ausschnitts).
         */
        public synchronized byte[] render(int index, int w, int h, float zoom, float panX, float panY) {
            PdfRenderer.Page p = renderer.openPage(index);
            Bitmap bmp = Bitmap.createBitmap(w, h, Bitmap.Config.ARGB_8888);
            try {
                bmp.eraseColor(Color.WHITE);
                float sx = w / (float) p.getWidth() * zoom, sy = h / (float) p.getHeight() * zoom;
                android.graphics.Matrix m = new android.graphics.Matrix();
                m.setScale(sx, sy);
                if (zoom > 1f) {
                    float maxX = p.getWidth() * sx - w, maxY = p.getHeight() * sy - h;
                    m.postTranslate(-maxX * clamp01(panX), -maxY * clamp01(panY));
                }
                p.render(bmp, null, m, PdfRenderer.Page.RENDER_MODE_FOR_DISPLAY);
            } finally { p.close(); }
            return toRgbaFlipped(bmp);
        }
        public synchronized void close() {
            try { renderer.close(); } catch (Exception ignored) {}
            try { fd.close(); } catch (Exception ignored) {}
        }
    }

    // ------------------------------------------------------------ Bilder (CBZ/ZIP)
    /**
     * Dekodiert JPG/PNG/WebP/GIF/BMP. Ergebnis: [0..3]=Breite (BE), [4..7]=Hoehe (BE), danach RGBA (Zeile 0 = unten).
     * maxW/maxH begrenzen die Aufloesung (Sampling + Skalierung, Seitenverhaeltnis bleibt).
     */
    public static byte[] decodeImage(byte[] data, int maxW, int maxH) {
        BitmapFactory.Options o = new BitmapFactory.Options();
        o.inJustDecodeBounds = true;
        BitmapFactory.decodeByteArray(data, 0, data.length, o);
        if (o.outWidth <= 0 || o.outHeight <= 0) return null;
        int sample = 1;
        while (o.outWidth / (sample * 2) >= maxW || o.outHeight / (sample * 2) >= maxH) sample *= 2;
        o = new BitmapFactory.Options();
        o.inSampleSize = sample;
        o.inPreferredConfig = Bitmap.Config.ARGB_8888;
        Bitmap bmp = BitmapFactory.decodeByteArray(data, 0, data.length, o);
        if (bmp == null) return null;
        float scale = Math.min(1f, Math.min(maxW / (float) bmp.getWidth(), maxH / (float) bmp.getHeight()));
        if (scale < 1f) {
            Bitmap s = Bitmap.createScaledBitmap(bmp, Math.max(1, Math.round(bmp.getWidth() * scale)), Math.max(1, Math.round(bmp.getHeight() * scale)), true);
            bmp.recycle(); bmp = s;
        }
        if (bmp.getConfig() != Bitmap.Config.ARGB_8888 || bmp.hasAlpha()) {
            Bitmap c = Bitmap.createBitmap(bmp.getWidth(), bmp.getHeight(), Bitmap.Config.ARGB_8888);
            Canvas cv = new Canvas(c); cv.drawColor(Color.WHITE); cv.drawBitmap(bmp, 0, 0, null);
            bmp.recycle(); bmp = c;
        }
        int w = bmp.getWidth(), h = bmp.getHeight();
        byte[] px = toRgbaFlipped(bmp);
        byte[] out = new byte[8 + px.length];
        out[0] = (byte) (w >>> 24); out[1] = (byte) (w >>> 16); out[2] = (byte) (w >>> 8); out[3] = (byte) w;
        out[4] = (byte) (h >>> 24); out[5] = (byte) (h >>> 16); out[6] = (byte) (h >>> 8); out[7] = (byte) h;
        System.arraycopy(px, 0, out, 8, px.length);
        return out;
    }

    // ------------------------------------------------------------ Hilfen
    private static float clamp01(float v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }

    private static byte[] toRgbaFlipped(Bitmap bmp) {
        int w = bmp.getWidth(), h = bmp.getHeight();
        ByteBuffer buf = ByteBuffer.allocate(w * h * 4).order(ByteOrder.nativeOrder());
        bmp.copyPixelsToBuffer(buf); // ARGB_8888 liegt im Speicher als RGBA
        bmp.recycle();
        byte[] src = buf.array();
        byte[] dst = new byte[src.length];
        int row = w * 4;
        for (int y = 0; y < h; y++) System.arraycopy(src, y * row, dst, (h - 1 - y) * row, row);
        return dst;
    }

    // ------------------------------------------------------------ Bildwiederholrate
    /** Sucht die im Headset eingestellte Bildwiederholrate (Settings-Tabellen + Systemeigenschaften), loggt alle Treffer. 0 = unbekannt. */
    public static float systemRefreshRate(Activity activity) {
        // PFD MR (Spatial Home) speichert die Einstellung als Settings.System "SpatialHome_SystemFrequencyMode"
        float found = 0f;
        try { found = rate(Settings.System.getString(activity.getContentResolver(), "SpatialHome_SystemFrequencyMode")); } catch (Exception e) { }
        StringBuilder log = new StringBuilder();
        String[] tables = { "system", "global", "secure" };
        for (String t : tables) {
            android.database.Cursor c = null;
            try {
                c = activity.getContentResolver().query(Uri.parse("content://settings/" + t), new String[] { "name", "value" }, null, null, null);
                while (c != null && c.moveToNext()) {
                    String n = c.getString(0), v = c.getString(1);
                    if (n == null) continue;
                    String l = n.toLowerCase();
                    if (l.contains("refresh") || l.contains("fps") || l.contains("hz") || l.contains("freq") || l.contains("frame_rate") || l.contains("framerate")) {
                        log.append(t).append(':').append(n).append('=').append(v).append("; ");
                        float f = rate(v);
                        if (f > 0 && found == 0) found = f;
                    }
                }
            } catch (Exception e) { log.append(t).append(" err ").append(e.getMessage()).append("; "); }
            finally { if (c != null) c.close(); }
        }
        try {
            Process pr = Runtime.getRuntime().exec("getprop");
            java.io.BufferedReader r = new java.io.BufferedReader(new java.io.InputStreamReader(pr.getInputStream()));
            String line;
            while ((line = r.readLine()) != null) {
                String l = line.toLowerCase();
                if (l.contains("refresh") || l.contains("fps") || l.contains("hz]") || l.contains("freq") || l.contains("framerate") || l.contains("frame_rate"))
                    log.append("prop ").append(line).append("; ");
            }
            r.close();
        } catch (Exception e) { log.append("getprop err ").append(e.getMessage()); }
        try { log.append("display=").append(activity.getWindowManager().getDefaultDisplay().getRefreshRate()); } catch (Exception e) { }
        log.append(" -> ").append(found);
        android.util.Log.i("PFDRate", log.toString());
        try {
            java.io.FileWriter w = new java.io.FileWriter(new File(activity.getExternalFilesDir(null), "refresh.txt"));
            w.write(log.toString()); w.close();
        } catch (Exception e) { }
        return found;
    }

    private static float rate(String v) {
        if (v == null) return 0f;
        try {
            float f = Float.parseFloat(v.trim());
            if (f == 60f || f == 72f || f == 75f || f == 80f || f == 90f || f == 120f) return f;
        } catch (Exception e) { }
        return 0f;
    }

    /** Messhilfe: letzte PerfStats-Zeilen dieses Prozesses mit Kennzeichnung in eine Datei anhaengen. */
    public static void dumpPerf(Activity activity, String label) {
        try {
            Process pr = Runtime.getRuntime().exec(new String[] { "logcat", "-d", "-t", "15", "-s", "PerfStats.cpp:I" });
            java.io.BufferedReader r = new java.io.BufferedReader(new java.io.InputStreamReader(pr.getInputStream()));
            StringBuilder b = new StringBuilder("## ").append(label).append('\n');
            String line;
            while ((line = r.readLine()) != null) if (line.contains("FPS=")) b.append(line).append('\n');
            r.close();
            java.io.FileWriter w = new java.io.FileWriter(new File(activity.getExternalFilesDir(null), "fovtest-result.txt"), true);
            w.write(b.toString()); w.close();
        } catch (Exception e) { android.util.Log.w("PFD", "dumpPerf " + e); }
    }

    // ------------------------------------------------------------ App-Update
    /** Ziel fuer das heruntergeladene Update (App-Cache, vom PfdApkProvider freigegeben). */
    public static String updateApkPath(Activity activity) { return PfdApkProvider.apk(activity).getAbsolutePath(); }

    /** Startet den System-Installer. "ok", "permission" (Einstellung "Unbekannte Apps" geoeffnet) oder Fehlertext. */
    public static String installApk(Activity activity) {
        try {
            if (Build.VERSION.SDK_INT >= 26 && !activity.getPackageManager().canRequestPackageInstalls()) {
                Intent s = new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:" + activity.getPackageName()));
                activity.startActivity(s);
                return "permission";
            }
            Uri uri = Uri.parse("content://" + activity.getPackageName() + ".apk/update.apk");
            Intent i = new Intent(Intent.ACTION_VIEW);
            i.setDataAndType(uri, "application/vnd.android.package-archive");
            i.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_ACTIVITY_NEW_TASK);
            activity.startActivity(i);
            return "ok";
        } catch (Exception e) { return e.toString(); }
    }

    /** Link im Browser oeffnen. */
    public static void openUrl(Activity activity, String url) {
        try { activity.startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse(url)).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)); } catch (Exception e) { }
    }
}
