package org.pfd.reader.unity;

import android.content.Context;
import android.media.AudioAttributes;
import android.media.MediaPlayer;
import android.os.Handler;
import android.os.Looper;
import java.util.*;

/**
 * Abspielen ueber Android-MediaPlayer (MP3, M4A/AAC, OGG, FLAC, WAV …). Jede Spur hat eine ID; Unity steuert
 * Start/Stopp/Pause und Lautstaerke. Beendete Spuren (Musik) werden ueber pollFinished() gemeldet.
 */
public final class PfdAudio {
    private PfdAudio() {}
    private static final Handler main = new Handler(Looper.getMainLooper());
    private static final Map<String, MediaPlayer> players = new HashMap<>();
    private static final Map<String, Float> volumes = new HashMap<>();
    private static final Set<String> paused = new HashSet<>();
    private static final List<String> finished = new ArrayList<>();
    private static final List<String> errors = new ArrayList<>();

    public static void play(final String id, final String path, final boolean loop, final float volume) {
        main.post(() -> {
            release(id);
            MediaPlayer p = new MediaPlayer();
            players.put(id, p); volumes.put(id, volume); paused.remove(id);
            try {
                p.setAudioAttributes(new AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_MEDIA).setContentType(AudioAttributes.CONTENT_TYPE_MUSIC).build());
                p.setDataSource(path);
                p.setLooping(loop);
                p.setOnPreparedListener(mp -> { if (players.get(id) != mp) return; float v = volumes.getOrDefault(id, 1f); mp.setVolume(v, v); if (!paused.contains(id)) mp.start(); });
                p.setOnCompletionListener(mp -> { if (players.get(id) == mp && !loop) { synchronized (finished) { finished.add(id); } } });
                p.setOnErrorListener((mp, w, e) -> { if (players.get(id) == mp) { players.remove(id); synchronized (errors) { errors.add(id); } } mp.release(); return true; });
                p.prepareAsync();
            } catch (Exception e) { players.remove(id); p.release(); synchronized (errors) { errors.add(id); } }
        });
    }

    public static void setVolume(final String id, final float v) {
        main.post(() -> { volumes.put(id, v); MediaPlayer p = players.get(id); if (p != null) try { p.setVolume(v, v); } catch (IllegalStateException ignored) {} });
    }

    public static void pause(final String id, final boolean pause) {
        main.post(() -> {
            MediaPlayer p = players.get(id);
            if (pause) paused.add(id); else paused.remove(id);
            if (p == null) return;
            try { if (pause && p.isPlaying()) p.pause(); else if (!pause && !p.isPlaying()) p.start(); } catch (IllegalStateException ignored) {}
        });
    }

    public static void stop(final String id) { main.post(() -> release(id)); }

    public static void stopAll() { main.post(() -> { for (String id : new ArrayList<>(players.keySet())) release(id); }); }

    private static void release(String id) {
        MediaPlayer p = players.remove(id); paused.remove(id);
        if (p != null) try { p.release(); } catch (Exception ignored) {}
    }

    /** Beendete Spuren (durch '\n' getrennt) seit dem letzten Aufruf. */
    public static String pollFinished() { synchronized (finished) { String s = String.join("\n", finished); finished.clear(); return s; } }
    public static String pollErrors() { synchronized (errors) { String s = String.join("\n", errors); errors.clear(); return s; } }

    /** Position/Dauer in ms ("pos/dur"), leer wenn unbekannt. */
    public static String progress(String id) {
        MediaPlayer p = players.get(id);
        if (p == null) return "";
        try { return p.getCurrentPosition() + "/" + p.getDuration(); } catch (Exception e) { return ""; }
    }
}
