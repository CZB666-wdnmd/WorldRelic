package local.worldrelic.files;

import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.UriPermission;
import android.net.Uri;
import android.os.Bundle;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.TextView;
import java.io.*;
import java.security.MessageDigest;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import org.json.JSONObject;

/** No Unity/VRChat dependencies. SAF-only read access, bounded immutable copies. */
public final class ImportActivity extends Activity {
    private static final String PREF = "external_documents";
    private static final String WORLD135 = "ADC8209B247A5AF8722376932B3B496D86DCFF400F30E632500BC32A4274C930";
    private static final String WORLD134 = "0A8CBB2D37EBE948EABA0B5C2EC49B837276B661D051DAFA679ABFB9216A0ED7";
    private static final String FONT = "9C32CC9CFF2D04AC95B0E8FCE51BA439083E473581B71CAE69AEAA8729E881B3";
    private static volatile String result = "";
    private static final ExecutorService worker = Executors.newSingleThreadExecutor();
    private TextView status;
    private boolean busy, picking;
    private String missing = "world", worldPath = "", fontPath = "";
    private File session;
    private SharedPreferences prefs;

    public static void begin(Activity caller) {
        result = "";
        caller.runOnUiThread(() -> caller.startActivity(new Intent(caller, ImportActivity.class)));
    }
    public static String poll() { return result; }
    private static boolean hasGrant(Context ctx, Uri uri) {
        for (UriPermission p : ctx.getContentResolver().getPersistedUriPermissions())
            if (p.isReadPermission() && p.getUri().equals(uri)) return true;
        return false;
    }
    public static boolean readable(Context ctx) {
        SharedPreferences p = ctx.getSharedPreferences(PREF, MODE_PRIVATE);
        for (String key : new String[]{"world", "font"}) {
            String value = p.getString(key, "");
            if (value.isEmpty()) return false;
            Uri uri = Uri.parse(value);
            try {
                if (!hasGrant(ctx, uri)) return false;
                try (InputStream in = ctx.getContentResolver().openInputStream(uri)) {
                    if (in == null || in.read() < 0) return false;
                }
            } catch (Exception e) { return false; }
        }
        return true;
    }
    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        prefs = getSharedPreferences(PREF, MODE_PRIVATE);
        // One fixed app-owned slot. Never delete provider data or broad cache dirs.
        session = new File(getCacheDir(), "approved-external-input");
        if (!session.isDirectory() && !session.mkdirs()) { finishFailure("无法创建应用私有缓存"); return; }
        LinearLayout root = new LinearLayout(this); root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(32, 48, 32, 24);
        status = new TextView(this); status.setTextSize(19);
        root.addView(status);
        button(root, "重新选择所需文件", () -> choose(missing));
        button(root, "更换世界 VRCW", () -> choose("world"));
        button(root, "更换字体", () -> choose("font"));
        button(root, "重试读取已选文件", () -> validateSaved(false));
        button(root, "退出", () -> finishFailure("已取消文件导入"));
        setContentView(root);
        picking = saved != null && saved.getBoolean("picking", false);
        missing = saved == null ? "world" : saved.getString("missing", "world");
        if (!picking) validateSaved(true);
    }
    private void button(LinearLayout root, String title, Runnable action) {
        Button b = new Button(this); b.setText(title);
        b.setOnClickListener(v -> { if (!busy && !picking) action.run(); }); root.addView(b);
    }
    @Override protected void onSaveInstanceState(Bundle out) {
        out.putBoolean("picking", picking); out.putString("missing", missing); super.onSaveInstanceState(out);
    }
    private void choose(String key) {
        if (busy || picking) return;
        missing = key; picking = true;
        status.setText(key.equals("world") ? "请选择批准的 Android v134 / v135 VRCW（不要选 Windows 包）" : "请选择 VL-PGothic-Regular.ttf 原文件（按 SHA256 识别）");
        Intent i = new Intent(Intent.ACTION_OPEN_DOCUMENT);
        i.addCategory(Intent.CATEGORY_OPENABLE); i.setType("*/*");
        i.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION);
        try { startActivityForResult(i, 61); }
        catch (Exception e) { picking = false; status.setText("系统文件选择器不可用，请启用系统文件应用后重试。"); }
    }
    @Override protected void onActivityResult(int code, int answer, Intent data) {
        super.onActivityResult(code, answer, data);
        if (code != 61) return;
        picking = false;
        if (answer != RESULT_OK || data == null || data.getData() == null) {
            status.setText("已取消选择。可以重试或退出，不会循环弹窗或使用内置资源。"); return;
        }
        Uri uri = data.getData(); String key = missing;
        if (!"content".equals(uri.getScheme())) { status.setText("只接受系统文件选择器提供的文档。"); return; }
        try {
            if ((data.getFlags() & Intent.FLAG_GRANT_READ_URI_PERMISSION) == 0) throw new SecurityException();
            getContentResolver().takePersistableUriPermission(uri, Intent.FLAG_GRANT_READ_URI_PERMISSION);
            if (!hasGrant(this, uri)) throw new SecurityException();
        } catch (Exception e) { status.setText("此提供方不能保留读取授权，请选择本地文件或支持持久授权的提供方。"); return; }
        busy = true; status.setText("正在复制并校验 " + key + "，请稍候……");
        worker.execute(() -> {
            try {
                String copied = copy(uri, key);
                String previous = prefs.getString(key, "");
                if (!prefs.edit().putString(key, uri.toString()).commit()) throw new IOException("保存文件授权引用失败");
                releaseIfUnused(previous);
                if (key.equals("world")) worldPath = copied; else fontPath = copied;
                runOnUiThread(() -> { busy = false; validateSaved(true); });
            } catch (Exception e) {
                releaseIfUnused(uri.toString());
                runOnUiThread(() -> { busy = false; status.setText("文件不可接受或无法读取：" + e.getMessage() + "\n请重新选择，未保存错误文件。"); });
            }
        });
    }
    private void releaseIfUnused(String value) {
        if (value.isEmpty() || value.equals(prefs.getString("world", "")) || value.equals(prefs.getString("font", ""))) return;
        try { getContentResolver().releasePersistableUriPermission(Uri.parse(value), Intent.FLAG_GRANT_READ_URI_PERMISSION); }
        catch (Exception ignored) { }
    }
    private String copy(Uri uri, String key) throws Exception {
        if (!hasGrant(this, uri)) throw new SecurityException("持久读取权限已失效");
        File pending = new File(session, key + ".pending");
        File approved = new File(session, key.equals("world") ? "world.vrcw" : "font.ttf");
        long limit = key.equals("world") ? 81674354L : 4149288L, count = 0;
        MessageDigest hash = MessageDigest.getInstance("SHA-256");
        try {
            try (InputStream in = getContentResolver().openInputStream(uri);
                 FileOutputStream out = new FileOutputStream(pending, false)) {
                if (in == null) throw new IOException("文件不再存在或不可读取");
                byte[] buffer = new byte[262144]; int n;
                while ((n = in.read(buffer)) != -1) {
                    count += n; if (count > limit) throw new IOException("文件大小不在批准范围");
                    out.write(buffer, 0, n); hash.update(buffer, 0, n);
                }
                out.getFD().sync();
            }
            StringBuilder hex = new StringBuilder(); for (byte b : hash.digest()) hex.append(String.format(java.util.Locale.ROOT, "%02X", b & 255));
            String digest = hex.toString();
            boolean ok = key.equals("world") ? (count == 81674354L && WORLD135.equals(digest)) ||
                (count == 79968961L && WORLD134.equals(digest)) : count == 4149288L && FONT.equals(digest);
            if (!ok) throw new IOException("SHA256 不匹配：仅接受批准的 Android VRCW / 唯一字体");
            if (!pending.renameTo(approved)) throw new IOException("提交已校验的私有副本失败");
            return approved.getCanonicalPath();
        } finally { if (pending.exists() && !pending.delete()) android.util.Log.w("ExternalDocuments", "Own pending copy cleanup failed"); }
    }
    private void validateSaved(boolean automaticPicker) {
        if (busy || picking) return;
        busy = true; status.setText("正在检查已保存的文件授权并校验内容……");
        worker.execute(() -> {
            for (String key : new String[]{"world", "font"}) {
                String value = prefs.getString(key, "");
                try {
                    if (value.isEmpty()) throw new IOException("尚未选择文件");
                    String path = copy(Uri.parse(value), key);
                    if (key.equals("world")) worldPath = path; else fontPath = path;
                } catch (Exception e) {
                    // Transient/offline providers can be retried without losing the old URI.
                    missing = key;
                    runOnUiThread(() -> { busy = false; status.setText("需要重新选择 " + key + "：" + e.getMessage()); if (automaticPicker) choose(key); });
                    return;
                }
            }
            try {
                JSONObject data = new JSONObject(); data.put("world", worldPath); data.put("font", fontPath);
                result = data.toString();
                android.util.Log.i("ExternalDocuments", "Both persisted documents verified; private snapshots ready");
                runOnUiThread(() -> { busy = false; finish(); });
            } catch (Exception e) { runOnUiThread(() -> { busy = false; status.setText("无法完成导入，请重试。"); }); }
        });
    }
    private void finishFailure(String message) {
        result = "{\"error\":\"" + message + "\"}"; finish();
    }
    @Override public void onBackPressed() { if (!busy) finishFailure("已取消文件导入"); }
}
