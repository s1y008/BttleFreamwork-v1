// ★ 技能编辑器 · 单位挂载层 —— 把「新建好的技能」真正挂到单位身上
//
// 【为什么必须有这一层】在 SkillData.json 里加一行 **不等于任何单位会用这个技能**：
//   Warrior.cs:320-343 的 m_SkillList 全部来自 m_WarriorBaseData.SkillArray（WarriorData.json），
//   AttackNpc.cs:291-309 同理来自 m_AttackNpcBaseData.SkillArray（AttackNpcData.json）。
//   新技能不写进某个单位的 SkillArray，它在游戏里就是一条死配置 ——
//   打不出来、也看不出来（属于铁律 5 明令禁止的静默降级），这一层就是为消灭它而存在。
//
// 【它的职责】把两张单位表解析成「单位 → 技能 id 列表」的内存态，
//   挂载 / 卸载只改内存，最后由 SaveAll 一次性**最小 diff** 落盘。
//
// 【为什么不塞进 SkillEditorModel】两张表的定位是「挂载关系」而不是「技能本体」：
//   主键都叫 ID，但这里唯一要改的列只有 SkillArray 一个。
//   收口在本文件，SkillEditorModel 继续只管 SkillData.json，两边互不污染。
//
// 【★ 性能】AttackNpcData.json 有 1460 个单位 / 673KB —— 只在 Refresh() 里解析一次并缓存，
//   IsMounted / Mount / Unmount / HasAnyDirty 之后全在内存里做，绝不重复读盘。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace SkillEditorKit
{
    /// <summary>
    /// 一个「单位 ↔ 技能」挂载项。
    ///
    /// 【★ 字段名是读出来的，不是猜的】两张表的单位主键都叫 <c>ID</c>、名称都叫 <c>Name</c>、
    ///   技能列表都叫 <c>SkillArray</c>（int 数组）—— 逐字节读过 WarriorData.json（21 个单位）
    ///   与 AttackNpcData.json（1460 个单位）的表头确认过。
    ///
    /// 【SkillArray 为什么用 List&lt;int&gt; 而不是 int[]】挂载 / 卸载要频繁增删元素，
    ///   数组每次增删都要整体重建；界面与落盘两侧都不在意这点空间开销。
    /// </summary>
    public sealed class UnitMountEntry
    {
        /// <summary>所在配置文件名："WarriorData.json" / "AttackNpcData.json"。</summary>
        public string FileName;

        /// <summary>true = WarriorData.json；false = AttackNpcData.json。</summary>
        public bool IsWarrior;

        /// <summary>单位 ID（两表的 ID 列）。</summary>
        public int UnitId;

        /// <summary>单位名称（两表的 Name 列），界面上给人看。</summary>
        public string UnitName;

        /// <summary>当前（内存态）技能 id 列表。落盘前的所有增删都只改这里。</summary>
        public List<int> SkillArray;
    }

    /// <summary>
    /// 单位挂载的读写层：解析两张单位表 → 内存态挂载 / 卸载 → 统一最小 diff 落盘。
    ///
    /// 全部静态方法。域重载（Enter Play Mode / 编译）后静态字段归零，
    /// 所以进入界面前必须先调 <see cref="Refresh"/>。
    /// </summary>
    public static class SkillUnitMountModel
    {
        private const string WarriorFile = "WarriorData.json";
        private const string AttackNpcFile = "AttackNpcData.json";

        private const string ConfigDir = "Assets/Resources/Config/";

        /// <summary>单位主键的 JSON 键名 —— 两表一致，且是全大写 ID 不是 Id。</summary>
        private const string IdKey = "ID";

        /// <summary>单位名称的 JSON 键名。</summary>
        private const string NameKey = "Name";

        /// <summary>技能 id 列表的 JSON 键名 —— 挂载关系唯一需要写回的列。</summary>
        private const string SkillArrayKey = "SkillArray";

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>全部单位（先 Warrior 后 AttackNpc），Refresh 时整体重建。</summary>
        private static readonly List<UnitMountEntry> _entries = new List<UnitMountEntry>();

        /// <summary>
        /// 与 _entries **逐下标对应**的「载入时快照」—— 判断脏、Revert 都靠它。
        ///
        /// 【为什么不把原始值挂在 UnitMountEntry 上】UnitMountEntry 是给界面用的数据行，
        ///   多带一份原始副本会让「这一行到底改没改」变成界面可以去误读的状态；
        ///   收口在本层的一张平行表里，界面永远碰不到它。
        /// </summary>
        private static readonly List<int[]> _originals = new List<int[]>();

        private static bool _loaded;

        // ══════════════════════════════════════════════════════
        // 生命周期
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 重新解析两张单位表。⚠ 会丢弃全部未落盘的挂载改动 —— 想留着先 <see cref="SaveAll"/>。
        /// </summary>
        public static void Refresh()
        {
            _entries.Clear();
            _originals.Clear();

            LoadFile(WarriorFile, true);
            LoadFile(AttackNpcFile, false);

            _loaded = true;
        }

        /// <summary>全部单位（只读视图）。</summary>
        public static IReadOnlyList<UnitMountEntry> All()
        {
            EnsureLoaded();
            return _entries;
        }

        // ══════════════════════════════════════════════════════
        // 挂载 / 卸载（只改内存）
        // ══════════════════════════════════════════════════════

        /// <summary>该单位当前是否挂着这个技能。</summary>
        public static bool IsMounted(UnitMountEntry e, int skillId)
        {
            if (e == null)
            {
                Debug.LogError("[SkillUnitMount] 查询失败：UnitMountEntry 为 null。");
                return false;
            }

            for (int i = 0; i < e.SkillArray.Count; i++)
            {
                if (e.SkillArray[i] == skillId) return true;
            }
            return false;
        }

        /// <summary>
        /// 给该单位挂上技能（追加到 SkillArray 末尾）。
        /// </summary>
        /// <returns>true = 挂上了；false = 本来就挂着（重复挂载不算错误，界面据此不刷新）。</returns>
        public static bool Mount(UnitMountEntry e, int skillId)
        {
            EnsureLoaded();

            if (e == null)
            {
                Debug.LogError("[SkillUnitMount] 挂载失败：UnitMountEntry 为 null。");
                return false;
            }

            // 重复挂载是幂等操作而不是错误 —— 但也不许默默塞第二份进去，
            // 否则 SkillArray 里出现重复 id，运行时会重复建技能
            if (IsMounted(e, skillId)) return false;

            e.SkillArray.Add(skillId);
            return true;
        }

        /// <summary>
        /// 从该单位身上卸下技能。
        /// </summary>
        /// <returns>true = 卸掉了；false = 本来就没挂。</returns>
        public static bool Unmount(UnitMountEntry e, int skillId)
        {
            EnsureLoaded();

            if (e == null)
            {
                Debug.LogError("[SkillUnitMount] 卸载失败：UnitMountEntry 为 null。");
                return false;
            }

            for (int i = 0; i < e.SkillArray.Count; i++)
            {
                if (e.SkillArray[i] != skillId) continue;
                e.SkillArray.RemoveAt(i);
                return true;
            }
            return false;
        }

        // ══════════════════════════════════════════════════════
        // 脏标记 / 落盘 / 回滚
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 是否有任意未落盘的挂载改动。
        ///
        /// 【为什么实时比而不是记一个 dirty 集合】挂上去又卸下来是常见操作，
        ///   记账式脏标记会把「净变化为零」误报成脏，进而产生一次毫无内容的写盘（连带一份备份）。
        ///   1460 个单位逐行比只是几千次整数比较，比一次误报写盘的代价小得多。
        /// </summary>
        public static bool HasAnyDirty()
        {
            EnsureLoaded();

            for (int i = 0; i < _entries.Count; i++)
            {
                if (IsEntryDirty(i)) return true;
            }
            return false;
        }

        /// <summary>
        /// 把全部改动落盘。
        /// </summary>
        /// <returns>成功落盘的文件数（0 ~ 2）。没改动的文件不写、不计入。</returns>
        public static int SaveAll()
        {
            EnsureLoaded();

            int saved = 0;
            saved += SaveFile(WarriorFile);
            saved += SaveFile(AttackNpcFile);
            return saved;
        }

        /// <summary>放弃全部未落盘的挂载改动，把内存态恢复成载入时 / 上次落盘时的样子。</summary>
        public static void Revert()
        {
            EnsureLoaded();

            for (int i = 0; i < _entries.Count; i++)
            {
                UnitMountEntry entry = _entries[i];
                int[] original = _originals[i];

                entry.SkillArray.Clear();
                for (int k = 0; k < original.Length; k++) entry.SkillArray.Add(original[k]);
            }
        }

        // ══════════════════════════════════════════════════════
        // 内部：解析
        // ══════════════════════════════════════════════════════

        private static void EnsureLoaded()
        {
            if (!_loaded) Refresh();
        }

        private static void LoadFile(string fileName, bool isWarrior)
        {
            string path = AbsolutePathOf(fileName);
            if (!File.Exists(path))
            {
                Debug.LogError("[SkillUnitMount] 载入失败：找不到配置文件 " + path
                    + "，该文件下的单位无法挂载技能。");
                return;
            }

            string text = ReadTextUtf8(path);
            if (text == null) return;   // 读取失败已在 ReadTextUtf8 里指名

            JArray array;
            try
            {
                array = JArray.Parse(text);
            }
            catch (Exception ex)
            {
                Debug.LogError("[SkillUnitMount] 载入失败：" + fileName + " 不是合法的 JSON 数组 —— " + ex.Message);
                return;
            }

            for (int i = 0; i < array.Count; i++)
            {
                JObject row = array[i] as JObject;
                if (row == null)
                {
                    Debug.LogError("[SkillUnitMount] " + fileName + " 的第 " + i
                        + " 行不是 JSON 对象，该单位无法挂载技能。");
                    continue;
                }

                JToken idToken;
                if (!row.TryGetValue(IdKey, out idToken) || idToken.Type != JTokenType.Integer)
                {
                    Debug.LogError("[SkillUnitMount] " + fileName + " 的第 " + i + " 行缺少整型字段 "
                        + IdKey + "，无法定位该单位。");
                    continue;
                }
                int unitId = idToken.Value<int>();

                JToken nameToken;
                string unitName = "";
                if (!row.TryGetValue(NameKey, out nameToken) || nameToken.Type != JTokenType.String)
                {
                    Debug.LogError("[SkillUnitMount] " + fileName + " 的单位 " + IdKey + "=" + unitId
                        + " 缺少字符串字段 " + NameKey + "，界面上会显示成无名单位。");
                }
                else
                {
                    unitName = nameToken.Value<string>();
                }

                JToken arrayToken;
                if (!row.TryGetValue(SkillArrayKey, out arrayToken) || arrayToken.Type != JTokenType.Array)
                {
                    // ★ 缺列必须点名，不许「跳过这个单位继续加载」而不出声 ——
                    //   静默跳过的后果是：界面上这个单位凭空消失，没人知道该去补哪一列
                    Debug.LogError("[SkillUnitMount] " + fileName + " 的单位 " + IdKey + "=" + unitId
                        + "(" + unitName + ") 缺少数组字段 " + SkillArrayKey
                        + "，该单位无法挂载技能（请补上这一列后重新 Refresh）。");
                    continue;
                }

                JArray skillArray = (JArray)arrayToken;
                int[] original = new int[skillArray.Count];
                bool broken = false;
                for (int k = 0; k < skillArray.Count; k++)
                {
                    JToken item = skillArray[k];
                    if (item.Type != JTokenType.Integer)
                    {
                        Debug.LogError("[SkillUnitMount] " + fileName + " 的单位 " + IdKey + "=" + unitId
                            + "(" + unitName + ") 的 " + SkillArrayKey + " 第 " + k + " 项不是整数（实际类型为 "
                            + item.Type + "），该单位无法挂载技能。");
                        broken = true;
                        break;
                    }
                    original[k] = item.Value<int>();
                }
                if (broken) continue;

                UnitMountEntry entry = new UnitMountEntry();
                entry.FileName = fileName;
                entry.IsWarrior = isWarrior;
                entry.UnitId = unitId;
                entry.UnitName = unitName;
                entry.SkillArray = new List<int>(skillArray.Count);
                for (int k = 0; k < original.Length; k++) entry.SkillArray.Add(original[k]);

                _entries.Add(entry);
                _originals.Add(original);
            }
        }

        // ══════════════════════════════════════════════════════
        // 内部：落盘
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 落盘一张表。
        /// </summary>
        /// <returns>1 = 成功落盘；0 = 没有改动 / 失败（失败原因已 LogError 指名）。</returns>
        private static int SaveFile(string fileName)
        {
            // ① 先收「这个文件里真正变了的行」—— 没变的文件一个字节都不碰，
            //    也就不产生备份、不刷新 mtime、不产生 diff
            List<int> dirtyIdx = new List<int>();
            for (int i = 0; i < _entries.Count; i++)
            {
                if (!string.Equals(_entries[i].FileName, fileName, StringComparison.Ordinal)) continue;
                if (IsEntryDirty(i)) dirtyIdx.Add(i);
            }
            if (dirtyIdx.Count == 0) return 0;

            string path = AbsolutePathOf(fileName);
            string assetPath = ConfigDir + fileName;

            if (!File.Exists(path))
            {
                Debug.LogError("[SkillUnitMount] 保存失败：找不到配置文件 " + path + "，改动仍留在内存中。");
                return 0;
            }

            // ② 物理备份 —— 沿用 SkillEditorModel.CreateBackup 的 .bak.<yyyyMMdd-HHmmss> 命名
            if (!CreateBackup(path))
            {
                Debug.LogError("[SkillUnitMount] 保存失败：" + fileName + " 备份不成功，为避免不可回滚的写入，已终止。");
                return 0;
            }

            string text = ReadTextUtf8(path);
            if (text == null) return 0;

            JArray array;
            try
            {
                array = JArray.Parse(text);
            }
            catch (Exception ex)
            {
                Debug.LogError("[SkillUnitMount] 保存失败：" + fileName + " 不是合法的 JSON 数组 —— " + ex.Message);
                return 0;
            }

            // ③ 只改 SkillArray 一个键 —— JObject 保留原有键顺序，替换已有键时位置不变，
            //    其余行、其余键一字不动
            for (int k = 0; k < dirtyIdx.Count; k++)
            {
                UnitMountEntry entry = _entries[dirtyIdx[k]];

                JObject row = FindRowById(array, entry.UnitId);
                if (row == null)
                {
                    Debug.LogError("[SkillUnitMount] 保存失败：" + fileName + " 中找不到 " + IdKey + " = "
                        + entry.UnitId + " 的单位行，无法写回 " + SkillArrayKey + "（已终止写入该文件）。");
                    return 0;
                }

                JArray mounted = new JArray();
                for (int s = 0; s < entry.SkillArray.Count; s++) mounted.Add(new JValue(entry.SkillArray[s]));
                row[SkillArrayKey] = mounted;
            }

            string output = array.ToString(Formatting.Indented);

            // Newtonsoft 的缩进换行取 Environment.NewLine；万一原文件用的是另一种风格，
            // 一次保存会把整个文件的换行全部改掉 ⇒ 统一回原文件风格，diff 才是干净的
            output = NormalizeNewLines(output, DetectNewLine(text));

            // 原文件末尾的换行（WarriorData.json 收尾是 \r\n、AttackNpcData.json 是 \n）也要保住，
            // 否则一次保存会顺手改掉最后一行 —— 已验证：保住尾部即可让「未改动的部分」往返零 diff
            output = PreserveTail(output, text);

            if (!WriteTextRobust(path, output))
            {
                Debug.LogError("[SkillUnitMount] 保存失败：写入 " + path + " 失败，改动仍留在内存中"
                    + "（可通过备份文件 " + path + ".bak.* 手动核对）。");
                return 0;
            }

            // 让 Unity 重新导入，否则 Resources.Load 拿到的仍是旧文本
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            // ④ 落盘成功 ⇒ 内存态升级为新的「原始态」，脏标记随之归零
            for (int k = 0; k < dirtyIdx.Count; k++) SnapshotOriginal(dirtyIdx[k]);
            return 1;
        }

        /// <summary>
        /// 物理备份，返回是否成功。
        ///
        /// 【★ 两个文件同时脏时备份名为什么不会互相覆盖】备份路径是「各自的文件名 + 时间戳」，
        ///   WarriorData.json.bak.<stamp> 与 AttackNpcData.json.bak.<stamp> 是两个不同路径，天然不冲突。
        ///   ★ 真正会冲突的是**同一个文件**在一秒内被连点保存两次（SaveAll 连点）——
        ///   所以撞上同名就追加序号 -2 / -3，绝不覆盖上一份（备份纪律：任何破坏性写盘前先留全尸）。
        /// </summary>
        private static bool CreateBackup(string path)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string backup = path + ".bak." + stamp;

            int seq = 1;
            while (File.Exists(backup))
            {
                seq++;
                backup = path + ".bak." + stamp + "-" + seq;
            }

            try
            {
                File.Copy(path, backup, true);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError("[SkillUnitMount] 备份失败：无法把 " + path + " 复制为 " + backup
                    + " —— " + ex.Message);
                return false;
            }
        }

        /// <summary>把该单位的当前列表记为新的「原始态」。</summary>
        private static void SnapshotOriginal(int index)
        {
            List<int> current = _entries[index].SkillArray;
            int[] copy = new int[current.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = current[i];
            _originals[index] = copy;
        }

        /// <summary>逐元素比对当前列表与原始快照。</summary>
        private static bool IsEntryDirty(int index)
        {
            List<int> current = _entries[index].SkillArray;
            int[] original = _originals[index];

            if (current.Count != original.Length) return true;
            for (int i = 0; i < original.Length; i++)
            {
                if (current[i] != original[i]) return true;
            }
            return false;
        }

        // ══════════════════════════════════════════════════════
        // 内部：文件读写
        // ══════════════════════════════════════════════════════

        private static string AbsolutePathOf(string fileName)
        {
            return Path.Combine(Application.dataPath, "Resources/Config/" + fileName);
        }

        /// <summary>按 <c>ID</c> 找到单位行；找不到返回 null（调用方负责 LogError 指名）。</summary>
        private static JObject FindRowById(JArray array, int unitId)
        {
            for (int i = 0; i < array.Count; i++)
            {
                JObject row = array[i] as JObject;
                if (row == null) continue;

                JToken token;
                if (!row.TryGetValue(IdKey, out token)) continue;
                if (token.Type != JTokenType.Integer) continue;

                if (token.Value<int>() == unitId) return row;
            }
            return null;
        }

        /// <summary>
        /// 把 UTF-8 文本写回磁盘。
        /// 【为什么分两步】本工程历史上出现过「Windows 上直接覆写已存在文件被拒绝」
        ///   （只读属性 / 杀软 / 其它进程持有句柄）。先直接写，失败退回「临时文件 + File.Copy 覆盖」。
        /// </summary>
        private static bool WriteTextRobust(string path, string text)
        {
            try
            {
                File.WriteAllText(path, text, Utf8NoBom);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SkillUnitMount] 直接写入 " + path + " 失败（" + ex.Message
                    + "），改用临时文件覆盖。");
            }

            string tmp = path + ".tmp." + DateTime.Now.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
            try
            {
                File.WriteAllText(tmp, text, Utf8NoBom);
                File.Copy(tmp, path, true);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError("[SkillUnitMount] 写入 " + path + " 失败：" + ex.Message);
                return false;
            }
            finally
            {
                TryDelete(tmp);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception)
            {
                // 临时文件删不掉不影响结果，下次写入会覆盖同名文件；不在这里刷屏
            }
        }

        /// <summary>
        /// 读取文本并按 UTF-8 解码。
        /// ⚠ 必须手动剥掉 BOM：UTF8Encoding.GetString 会把 BOM 解成 U+FEFF 留在串首，
        ///   带着它 JArray.Parse 会直接失败。这里统一剥掉，写入时也不加 BOM（Utf8NoBom）。
        /// </summary>
        private static string ReadTextUtf8(string path)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                string text = Encoding.UTF8.GetString(bytes);
                if (text.Length > 0 && text[0] == '\uFEFF') return text.Substring(1);
                return text;
            }
            catch (Exception ex)
            {
                Debug.LogError("[SkillUnitMount] 读取文件失败：" + path + " —— " + ex.Message);
                return null;
            }
        }

        /// <summary>猜原文件用的是 CRLF 还是 LF —— 没换行的文件就跟着当前平台走。</summary>
        private static string DetectNewLine(string text)
        {
            int index = text.IndexOf('\n');
            if (index < 0) return Environment.NewLine;
            return (index > 0 && text[index - 1] == '\r') ? "\r\n" : "\n";
        }

        private static string NormalizeNewLines(string text, string newLine)
        {
            if (string.Equals(text, string.Empty, StringComparison.Ordinal)) return text;
            if (string.Equals(newLine, Environment.NewLine, StringComparison.Ordinal)) return text;

            // 先统一成 LF 再换成目标风格，避免在已经是 \r\n 的文本上二次替换出 \r\r\n
            return text.Replace("\r\n", "\n").Replace("\n", newLine);
        }

        /// <summary>
        /// 把原文件末尾的换行原样补回输出串。
        /// 【为什么要单独处理】JArray.ToString(Formatting.Indented) 不会产出结尾换行，
        ///   而两张表一个以 \r\n 收尾、一个以 \n 收尾 —— 不补回来，每次保存都会改掉最后一行。
        /// </summary>
        private static string PreserveTail(string output, string originalText)
        {
            int i = originalText.Length - 1;
            while (i >= 0 && (originalText[i] == '\r' || originalText[i] == '\n')) i--;

            string tail = originalText.Substring(i + 1);
            return string.IsNullOrEmpty(tail) ? output : output + tail;
        }
    }
}
