package org.pfd.reader.unity;

import android.content.Context;
import android.content.SharedPreferences;
import android.net.nsd.NsdManager;
import android.net.nsd.NsdServiceInfo;
import android.net.wifi.WifiManager;
import android.os.Handler;
import android.os.Looper;
import android.os.SystemClock;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;
import com.hierynomus.msdtyp.AccessMask;
import com.hierynomus.msfscc.fileinformation.FileIdBothDirectoryInformation;
import com.hierynomus.mssmb2.SMB2CreateDisposition;
import com.hierynomus.mssmb2.SMB2CreateOptions;
import com.hierynomus.mssmb2.SMB2ShareAccess;
import com.hierynomus.security.bc.BCSecurityProvider;
import com.hierynomus.smbj.SMBClient;
import com.hierynomus.smbj.SmbConfig;
import com.hierynomus.smbj.auth.AuthenticationContext;
import com.hierynomus.smbj.connection.Connection;
import com.hierynomus.smbj.session.Session;
import com.hierynomus.smbj.share.DiskShare;
import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.security.KeyStore;
import java.util.*;
import java.util.concurrent.TimeUnit;
import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

/**
 * SMB-Zugriff fuer den Unity-Import (portiert aus der nativen App): Geraetesuche (mDNS + WS-Discovery),
 * Freigaben, Ordner lesen, Datei kopieren, optional verschluesselt gespeichertes Passwort (AndroidKeyStore).
 * Alle blockierenden Methoden werden von Unity auf einem Hintergrund-Thread aufgerufen.
 */
public final class PfdSmb {
    private PfdSmb() {}
    private static Context ctx;
    public static void init(android.app.Activity c) { if (ctx == null) ctx = c.getApplicationContext(); }

    // ------------------------------------------------------------ Adressen
    /** host[:port] | \\host\share\ordner | smb://host/share/ordner -> {host, port, share, folder} */
    public static String[] parse(String server, String shareName) {
        String raw = server.trim().replace('\\', '/');
        if (raw.regionMatches(true, 0, "smb://", 0, 6)) raw = raw.substring(6);
        while (raw.startsWith("/")) raw = raw.substring(1);
        int slash = raw.indexOf('/');
        String authority = slash < 0 ? raw : raw.substring(0, slash);
        String remote = slash < 0 ? shareName.trim().replace('\\', '/') : raw.substring(slash + 1);
        while (remote.startsWith("/")) remote = remote.substring(1);
        String[] comps = remote.split("/+");
        String share = comps.length == 0 ? "" : comps[0];
        StringBuilder folder = new StringBuilder();
        for (int i = 1; i < comps.length; i++) {
            String c = comps[i];
            if (c.equals(".") || c.equals("..") || c.indexOf('\0') >= 0) throw new IllegalArgumentException("Ungültiger Netzwerkpfad");
            if (!c.isEmpty()) { if (folder.length() > 0) folder.append('\\'); folder.append(c); }
        }
        String host = authority; int port = 445;
        if (host.startsWith("[")) {
            int end = host.indexOf(']'); if (end < 0) throw new IllegalArgumentException("Ungültiger Server");
            String tail = host.substring(end + 1);
            if (tail.startsWith(":")) port = Integer.parseInt(tail.substring(1));
            host = host.substring(1, end);
        } else if (host.indexOf(':') >= 0 && host.indexOf(':') == host.lastIndexOf(':')) {
            int colon = host.indexOf(':'); port = Integer.parseInt(host.substring(colon + 1)); host = host.substring(0, colon);
        }
        if (host.isEmpty() || host.contains("@") || host.matches(".*\\s.*") || port < 1 || port > 65535) throw new IllegalArgumentException("Ungültiger Server");
        return new String[]{host, String.valueOf(port), share, folder.toString()};
    }

    // ------------------------------------------------------------ Verbindung
    private static final class Conn implements AutoCloseable {
        final SMBClient client; Connection connection; Session session; DiskShare share;
        Conn(String host, int port, String shareName, String user, String password, String domain) throws IOException {
            client = new SMBClient(SmbConfig.builder().withSecurityProvider(new BCSecurityProvider())
                .withTimeout(20, TimeUnit.SECONDS).withSoTimeout(25, TimeUnit.SECONDS).build());
            try {
                status = "Server wird kontaktiert …";
                connection = client.connect(host, port);
                status = "Anmeldung läuft …";
                session = connection.authenticate(user == null || user.isEmpty() ? AuthenticationContext.anonymous()
                    : new AuthenticationContext(user, password == null ? new char[0] : password.toCharArray(), domain == null ? "" : domain));
                if (shareName != null && !shareName.isEmpty()) { status = "Freigabe wird geöffnet …"; share = (DiskShare) session.connectShare(shareName); }
            } catch (Exception e) { close(); throw new IOException(clean(e, password), e); }
        }
        @Override public void close() {
            try { if (share != null) share.close(); } catch (Exception ignored) {}
            try { if (session != null) session.close(); } catch (Exception ignored) {}
            try { if (connection != null) connection.close(); } catch (Exception ignored) {}
            client.close();
        }
    }

    private static String clean(Exception e, String password) {
        String d = e.getMessage() == null ? e.getClass().getSimpleName() : e.getMessage();
        if (password != null && !password.isEmpty()) d = d.replace(password, "••••");
        if (d.contains("STATUS_LOGON_FAILURE")) return "Anmeldung fehlgeschlagen (Benutzer/Passwort prüfen)";
        if (d.contains("STATUS_ACCESS_DENIED")) return "Zugriff verweigert";
        if (d.contains("STATUS_BAD_NETWORK_NAME")) return "Freigabe nicht gefunden";
        if (e instanceof UnknownHostException) return "Server nicht gefunden";
        if (e instanceof SocketTimeoutException || d.contains("timed out")) return "Zeitüberschreitung – Server erreichbar?";
        return d;
    }

    public static volatile String status = "";
    public static volatile long copied = 0;
    private static volatile boolean cancel;
    public static void cancel() { cancel = true; }

    /** Sichtbare Freigaben (ohne $-Freigaben), durch '\n' getrennt. */
    public static String shares(String server, String user, String password, String domain) throws Exception {
        String[] a = parse(server, "");
        try (Conn c = new Conn(a[0], Integer.parseInt(a[1]), "", user, password, domain)) {
            status = "Freigaben werden gelesen …";
            com.rapid7.client.dcerpc.transport.RPCTransport t = com.rapid7.client.dcerpc.transport.SMBTransportFactories.SRVSVC.getTransport(c.session);
            com.rapid7.client.dcerpc.mssrvs.ServerService svc = new com.rapid7.client.dcerpc.mssrvs.ServerService(t);
            ArrayList<String> names = new ArrayList<>();
            for (com.rapid7.client.dcerpc.mssrvs.dto.NetShareInfo0 info : svc.getShares0()) { String n = info.getNetName(); if (!n.endsWith("$")) names.add(n); }
            Collections.sort(names, String.CASE_INSENSITIVE_ORDER);
            return String.join("\n", names);
        } catch (IOException e) { throw e; } catch (Exception e) { throw new IOException(clean(e, password), e); }
    }

    /** Ordnerinhalt: je Zeile "D|name|0" oder "F|name|bytes"; Ordner zuerst, alphabetisch. */
    public static String list(String server, String share, String folder, String user, String password, String domain) throws Exception {
        String[] a = parse(server, share);
        try (Conn c = new Conn(a[0], Integer.parseInt(a[1]), a[2], user, password, domain)) {
            status = "Dateien werden gelesen …";
            ArrayList<String[]> items = new ArrayList<>();
            for (FileIdBothDirectoryInformation it : c.share.list(folder == null ? "" : folder, "*")) {
                String n = it.getFileName();
                if (n.equals(".") || n.equals("..") || n.indexOf('/') >= 0 || n.indexOf('\\') >= 0) continue;
                boolean dir = (it.getFileAttributes() & 16) != 0;
                items.add(new String[]{dir ? "D" : "F", n, String.valueOf(dir ? 0 : it.getEndOfFile())});
                if (items.size() > 10000) throw new IOException("Zu viele Einträge");
            }
            items.sort((x, y) -> x[0].equals(y[0]) ? x[1].compareToIgnoreCase(y[1]) : x[0].compareTo(y[0]));
            StringBuilder sb = new StringBuilder();
            for (String[] i : items) sb.append(i[0]).append('|').append(i[1]).append('|').append(i[2]).append('\n');
            return sb.toString();
        } catch (IOException e) { throw e; } catch (Exception e) { throw new IOException(clean(e, password), e); }
    }

    /** Kopiert eine Datei nach destDir (vorhandene Dateien bleiben: " (1)" usw.). Ergebnis: Zielpfad. */
    public static String copy(String server, String share, String folder, String name, String user, String password, String domain, String destDir) throws Exception {
        String[] a = parse(server, share);
        cancel = false; copied = 0;
        try (Conn c = new Conn(a[0], Integer.parseInt(a[1]), a[2], user, password, domain)) {
            String remote = folder == null || folder.isEmpty() ? name : folder + "\\" + name;
            status = "Kopieren …";
            try (com.hierynomus.smbj.share.File f = c.share.openFile(remote, EnumSet.of(AccessMask.FILE_READ_DATA), null, SMB2ShareAccess.ALL,
                    SMB2CreateDisposition.FILE_OPEN, EnumSet.of(SMB2CreateOptions.FILE_NON_DIRECTORY_FILE)); InputStream in = f.getInputStream()) {
                return save(in, name, new File(destDir));
            }
        } catch (IOException e) { throw e; } catch (Exception e) { throw new IOException(clean(e, password), e); }
    }

    static String save(InputStream in, String name, File dest) throws IOException {
        String safe = name.replaceAll("[<>:\"|?*\\\\/\\p{Cntrl}]", "_");
        if (safe.length() > 180) { int dot = safe.lastIndexOf('.'); safe = safe.substring(0, 160) + (dot > 0 ? safe.substring(dot) : ""); }
        if (!dest.isDirectory() && !dest.mkdirs()) throw new IOException("Zielordner nicht verfügbar");
        File tmp = File.createTempFile(".download-", ".part", dest);
        try {
            try (OutputStream out = new FileOutputStream(tmp)) {
                byte[] buf = new byte[65536]; int n;
                while ((n = in.read(buf)) != -1) { if (cancel) throw new IOException("Abgebrochen"); out.write(buf, 0, n); copied += n; }
            }
            int dot = safe.lastIndexOf('.'); if (dot < 0) dot = safe.length();
            for (int i = 0; i < 10000; i++) {
                File target = new File(dest, i == 0 ? safe : safe.substring(0, dot) + " (" + i + ")" + safe.substring(dot));
                try { Files.move(tmp.toPath(), target.toPath()); return target.getAbsolutePath(); }
                catch (java.nio.file.FileAlreadyExistsException e) { /* vorhandene Datei behalten */ }
            }
            throw new IOException("Zu viele gleichnamige Dateien");
        } finally { if (tmp.exists()) tmp.delete(); }
    }

    // ------------------------------------------------------------ Geraetesuche
    private static final Handler main = new Handler(Looper.getMainLooper());
    private static final LinkedHashMap<String, String> found = new LinkedHashMap<>();
    private static volatile boolean searching;
    private static volatile int generation;
    private static NsdManager.DiscoveryListener nsdListener;
    private static WifiManager.MulticastLock multicast;
    private static volatile DatagramSocket socket;

    /** Startet eine ~8 s lange Suche (mDNS _smb._tcp + WS-Discovery). */
    public static void startDiscovery() {
        main.post(() -> {
            stopInternal();
            final int run = ++generation;
            synchronized (found) { found.clear(); }
            searching = true;
            try {
                WifiManager wifi = (WifiManager) ctx.getSystemService(Context.WIFI_SERVICE);
                multicast = wifi.createMulticastLock("PfdReaderDiscovery"); multicast.setReferenceCounted(false); multicast.acquire();
            } catch (Exception ignored) {}
            final NsdManager nsd = (NsdManager) ctx.getSystemService(Context.NSD_SERVICE);
            nsdListener = new NsdManager.DiscoveryListener() {
                public void onDiscoveryStarted(String t) {} public void onDiscoveryStopped(String t) {}
                public void onStartDiscoveryFailed(String t, int e) {} public void onStopDiscoveryFailed(String t, int e) {}
                public void onServiceLost(NsdServiceInfo i) {}
                public void onServiceFound(NsdServiceInfo i) {
                    try {
                        nsd.resolveService(i, new NsdManager.ResolveListener() {
                            public void onResolveFailed(NsdServiceInfo s, int err) {}
                            public void onServiceResolved(NsdServiceInfo s) {
                                if (generation != run || s.getHost() == null) return;
                                String h = s.getHost().getHostAddress(); int p = s.getPort();
                                add(h.indexOf(':') >= 0 ? "[" + h + "]:" + p : (p == 445 ? h : h + ":" + p), s.getServiceName());
                            }
                        });
                    } catch (Exception ignored) {}
                }
            };
            try { nsd.discoverServices("_smb._tcp.", NsdManager.PROTOCOL_DNS_SD, nsdListener); } catch (Exception ignored) {}
            new Thread(() -> wsProbe(run), "PFD-Discovery").start();
            main.postDelayed(() -> { if (generation == run) stopInternal(); }, 8000);
        });
    }

    private static void add(String address, String label) { synchronized (found) { if (!found.containsKey(address) || found.get(address).equals(address)) found.put(address, label); } }

    /** "1" (sucht noch) oder "0", danach je Zeile "adresse\tname". */
    public static String pollDiscovery() {
        StringBuilder sb = new StringBuilder(searching ? "1" : "0");
        synchronized (found) { for (Map.Entry<String, String> e : found.entrySet()) sb.append('\n').append(e.getKey()).append('\t').append(e.getValue()); }
        return sb.toString();
    }

    public static void stopDiscovery() { main.post(PfdSmb::stopInternal); }

    private static void stopInternal() {
        searching = false; generation++;
        DatagramSocket s = socket; if (s != null) s.close();
        if (nsdListener != null) { try { ((NsdManager) ctx.getSystemService(Context.NSD_SERVICE)).stopServiceDiscovery(nsdListener); } catch (Exception ignored) {} nsdListener = null; }
        if (multicast != null && multicast.isHeld()) multicast.release();
    }

    private static void wsProbe(int run) {
        String id = "uuid:" + UUID.randomUUID();
        String req = "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\" xmlns:a=\"http://schemas.xmlsoap.org/ws/2004/08/addressing\" xmlns:d=\"http://schemas.xmlsoap.org/ws/2005/04/discovery\"><s:Header><a:MessageID>" + id + "</a:MessageID><a:To>urn:schemas-xmlsoap-org:ws:2005:04:discovery</a:To><a:Action>http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</a:Action></s:Header><s:Body><d:Probe/></s:Body></s:Envelope>";
        try (DatagramSocket udp = new DatagramSocket()) {
            socket = udp; udp.setSoTimeout(700);
            byte[] data = req.getBytes(StandardCharsets.UTF_8);
            udp.send(new DatagramPacket(data, data.length, InetAddress.getByName("239.255.255.250"), 3702));
            long end = SystemClock.uptimeMillis() + 7500;
            while (generation == run && SystemClock.uptimeMillis() < end) {
                DatagramPacket reply = new DatagramPacket(new byte[16384], 16384);
                try { udp.receive(reply); } catch (SocketTimeoutException t) { continue; }
                String xml = new String(reply.getData(), 0, reply.getLength(), StandardCharsets.UTF_8);
                if (xml.contains(id) && xml.contains("ProbeMatch")) {
                    String host = reply.getAddress().getHostAddress(), label = host;
                    try { String n = reply.getAddress().getCanonicalHostName(); if (n != null && !n.equals(host)) label = n; } catch (Exception ignored) {}
                    add(host, label);
                }
            }
        } catch (Exception ignored) {}
    }

    // ------------------------------------------------------------ Passwort (opt-in, geraetegebunden)
    private static final String ALIAS = "pfd.reader.unity.smb.v1";
    private static SharedPreferences prefs() { return ctx.getSharedPreferences("pfd_smb", Context.MODE_PRIVATE); }
    private static SecretKey key() throws Exception {
        KeyStore ks = KeyStore.getInstance("AndroidKeyStore"); ks.load(null);
        if (ks.containsAlias(ALIAS)) return (SecretKey) ks.getKey(ALIAS, null);
        KeyGenerator g = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore");
        g.init(new KeyGenParameterSpec.Builder(ALIAS, KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
            .setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).build());
        return g.generateKey();
    }
    private static String slot(String identity) { return "pw:" + Integer.toHexString(identity.hashCode()); }
    public static boolean savePassword(String identity, String password) {
        try {
            Cipher c = Cipher.getInstance("AES/GCM/NoPadding"); c.init(Cipher.ENCRYPT_MODE, key()); c.updateAAD(identity.getBytes(StandardCharsets.UTF_8));
            String enc = Base64.encodeToString(c.doFinal(password.getBytes(StandardCharsets.UTF_8)), Base64.NO_WRAP);
            String iv = Base64.encodeToString(c.getIV(), Base64.NO_WRAP);
            return prefs().edit().putString(slot(identity), iv + ":" + enc).commit();
        } catch (Exception e) { return false; }
    }
    public static String loadPassword(String identity) {
        try {
            String v = prefs().getString(slot(identity), null); if (v == null) return "";
            String[] p = v.split(":", 2);
            Cipher c = Cipher.getInstance("AES/GCM/NoPadding");
            c.init(Cipher.DECRYPT_MODE, key(), new GCMParameterSpec(128, Base64.decode(p[0], Base64.NO_WRAP)));
            c.updateAAD(identity.getBytes(StandardCharsets.UTF_8));
            return new String(c.doFinal(Base64.decode(p[1], Base64.NO_WRAP)), StandardCharsets.UTF_8);
        } catch (Exception e) { return ""; }
    }
    public static boolean hasPassword(String identity) { return ctx != null && prefs().contains(slot(identity)); }
    public static void forgetPassword(String identity) { prefs().edit().remove(slot(identity)).commit(); }
}
