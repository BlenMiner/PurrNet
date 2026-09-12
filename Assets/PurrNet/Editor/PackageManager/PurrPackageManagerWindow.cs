using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace PurrNet.Editor
{
    public class PurrPackageManagerWindow : EditorWindow
    {
        private string _errorMessage;
        private bool _isLoading;

        private int _selectedIndex = -1;
        private Vector2 _listScrollPosition;
        private Vector2 _detailScrollPosition;
        private string _searchQuery = string.Empty;

        private float _splitWidth = 240f;
        private bool _isDraggingSplitter;
        private Rect _cachedSplitterRect;
        private int _prevKeyboardControl;
        // -1 = "follow the installed version". Once the user picks something else in the
        // dropdown the *Touched flag latches, so re-renders (and async installed-version
        // refreshes) stop overriding their choice. Both reset whenever the selected package
        // changes or an install completes.
        private int _releasePopupIndex = -1;
        private int _devPopupIndex = -1;
        private bool _releasePopupTouched;
        private bool _devPopupTouched;
        private bool _isUpdatingAll;
        [NonSerialized] private bool _isRunningUpdateBatch;
        private bool _isRegisteringRepository;
        private int _updatableCount;
        private readonly HashSet<string> _activePackageOperations = new();
        private string _currentRepoOwner;
        private string _currentRepoName;
        private string _currentRepoDetectionError;

        private PackagesResponse _packages;
        private EntitlementsResponse _entitlements;
        private PurrUserProfile _userProfile;

        // Cached sorted list rebuilt each frame from _packages
        private readonly List<(PackageInfo pkg, VersionInfo release, VersionInfo dev)> _sortedPackages = new();
        private readonly List<(string name, int startIndex, int count)> _categories = new();
        private readonly Dictionary<string, int> _categoryUpdateCounts = new();

        private static readonly Color _headerBg = new Color(0.17f, 0.17f, 0.17f, 1f);
        private static readonly Color _accentColor = new Color(0.4f, 0.7f, 1f, 1f);
        private static readonly Color _installedColor = new Color(0.4f, 0.8f, 0.4f, 1f);
        private static readonly Color _updateColor = new Color(1f, 0.76f, 0.28f, 1f);
        private static readonly Color _frozenColor = new Color(0.95f, 0.5f, 0.5f, 1f);
        private static readonly Color _separatorColor = new Color(0.13f, 0.13f, 0.13f, 1f);
        private static readonly Color _listBg = new Color(0.2f, 0.2f, 0.2f, 1f);
        private static readonly Color _selectedBg = new Color(0.17f, 0.36f, 0.53f, 1f);
        private static readonly Color _hoverBg = new Color(0.26f, 0.26f, 0.26f, 1f);
        private static readonly Color _noAccessColor = new Color(0.95f, 0.45f, 0.45f, 1f);
        private static readonly Dictionary<string, Color> _tierColors = new()
        {
            { "house-cat", new Color(0.4f, 0.7f, 1f, 1f) },
            { "royal-british", new Color(0.7f, 0.45f, 0.9f, 1f) },
            { "studio", new Color(0.85f, 0.65f, 0.3f, 1f) },
            { "admin", new Color(1f, 0.35f, 0.5f, 1f) },
        };
        private static readonly Color _categoryBg = new Color(0.16f, 0.16f, 0.16f, 1f);
        private static readonly Color _selectedAccent = new Color(0.35f, 0.65f, 0.95f, 1f);

        [NonSerialized] private GUIStyle _descStyle;
        [NonSerialized] private GUIStyle _badgeStyle;
        [NonSerialized] private GUIStyle _smallLabelStyle;
        [NonSerialized] private GUIStyle _listItemStyle;
        [NonSerialized] private GUIStyle _listItemDetailStyle;
        [NonSerialized] private GUIStyle _earlyAccessListStyle;
        [NonSerialized] private GUIStyle _categoryStyle;
        [NonSerialized] private GUIStyle _categoryUpdateStyle;
        [NonSerialized] private GUIStyle _detailTitleStyle;
        [NonSerialized] private GUIStyle _releaseNotesStyle;
        [NonSerialized] private SearchField _searchField;
        private Texture2D _logo;

        private const int StudiosEntryIndex = int.MaxValue;

        private const float SplitMargin = 80f;
        private const float ListItemHeight = 28f;
        private const float CategoryHeaderHeight = 20f;
        private const float CategoryGap = 8f;
        private const float SearchAreaHeight = 26f;
        private const float SplitterWidth = 6f;
        private const string CategoryFoldoutPreferencePrefix = "PurrNet.PackageManager.CategoryExpanded.";
        private const string PackageWebsiteBaseUrl = "https://purrnet.dev/packages/";
        private const string PackageAdminUrl = "https://purrnet.dev/admin/packages";
        private const string PendingUpdateBatchSessionKey = "PurrNet.PackageManager.PendingUpdateBatch";
        private static readonly string[] _busyFrames = { "|", "/", "-", "\\" };

        [InitializeOnLoadMethod]
        private static void InitializeUpdateBatchReloadGuard()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= ClearBatchProgressBeforeReload;
            AssemblyReloadEvents.beforeAssemblyReload += ClearBatchProgressBeforeReload;

            // A native progress dialog can survive the managed domain that created it.
            if (HasPendingUpdateBatch())
                EditorUtility.ClearProgressBar();
        }

        private static void ClearBatchProgressBeforeReload()
        {
            if (HasPendingUpdateBatch())
                EditorUtility.ClearProgressBar();
        }

        [MenuItem("Tools/PurrNet/PurrNet Packages %#&p", false, -101)]
        public static void ShowWindow()
        {
            var window = GetWindow<PurrPackageManagerWindow>();
            var icon = Resources.Load<Texture2D>("purricon");
            window.titleContent = new GUIContent("PurrNet Packages", icon);
            window.minSize = new Vector2(520, 350);
        }

        [MenuItem("Tools/PurrNet/PurrNet for Studios", false, 1000)]
        private static void OpenStudiosPage()
        {
            Application.OpenURL("https://purrnet.dev/studios");
        }

        private void OnEnable()
        {
            wantsMouseMove = true;
            _logo = Resources.Load<Texture2D>("purricon");
            _userProfile = new PurrUserProfile(Repaint);
            _userProfile.Refresh();
            DetectCurrentRepository();
            PurrPackageManagerAuth.onAuthChanged += onAuthChanged;
            UnityEditor.PackageManager.Events.registeredPackages += OnRegisteredPackages;
            _isUpdatingAll = HasPendingUpdateBatch();
            LoadData();
        }

        private void OnInspectorUpdate()
        {
            if (_isUpdatingAll || _activePackageOperations.Count > 0)
                Repaint();
        }

        private void OnDisable()
        {
            PurrPackageManagerAuth.onAuthChanged -= onAuthChanged;
            UnityEditor.PackageManager.Events.registeredPackages -= OnRegisteredPackages;
        }

        private void OnRegisteredPackages(UnityEditor.PackageManager.PackageRegistrationEventArgs args)
        {
            if (HasPendingUpdateBatch() && !_isRunningUpdateBatch && _packages?.Packages != null)
                _ = ResumePendingUpdateBatch();
            Repaint();
        }

        private void onAuthChanged()
        {
            PurrPackageManagerCache.Invalidate();
            _entitlements = null;
            _errorMessage = null;
            _userProfile?.Refresh();
            LoadData();
            Repaint();
        }

        private bool HasActivePackageOperation()
        {
            return _activePackageOperations.Count > 0;
        }

        private bool CanStartPackageOperation()
        {
            return !_isLoading && !_isUpdatingAll && !HasActivePackageOperation();
        }

        private bool TryBeginPackageOperation(string operationKey)
        {
            if (_isLoading || _isUpdatingAll || HasActivePackageOperation() || string.IsNullOrEmpty(operationKey))
                return false;

            _activePackageOperations.Add(operationKey);
            Repaint();
            return true;
        }

        private void EndPackageOperation(string operationKey)
        {
            if (!string.IsNullOrEmpty(operationKey))
                _activePackageOperations.Remove(operationKey);

            Repaint();
        }

        private bool IsPackageOperationActive(string operationKey)
        {
            return !string.IsNullOrEmpty(operationKey) && _activePackageOperations.Contains(operationKey);
        }

        private static string GetPackageOperationKey(PackageInfo package, VersionInfo version)
        {
            return $"{package?.Id ?? package?.GetUpmPackageName() ?? "unknown"}:{version?.Id ?? version?.Version ?? "unknown"}";
        }

        private static string GetPackageOperationKey(PackageInfo package, string channelLabel)
        {
            return $"{package?.Id ?? package?.GetUpmPackageName() ?? "unknown"}:{channelLabel}";
        }

        private static string BusyLabel(string label)
        {
            var frame = _busyFrames[(int)(EditorApplication.timeSinceStartup * 8d) % _busyFrames.Length];
            return $"{frame} {label}";
        }

        private static string FormatTierName(string tier)
        {
            if (string.IsNullOrEmpty(tier)) return null;
            switch (tier)
            {
                case "house-cat": return "House Cat";
                case "royal-british": return "Royal British";
                case "studio": return "Studio";
                case "admin": return "Admin Only";
                case "free": return null;
                default: return tier;
            }
        }

        private void InitStyles()
        {
            _searchField ??= new SearchField();

            if (_detailTitleStyle != null && _listItemDetailStyle != null &&
                _releaseNotesStyle != null && _categoryUpdateStyle != null)
                return;

            _descStyle = new GUIStyle(EditorStyles.label)
            {
                wordWrap = true,
                fontSize = 11,
                normal = { textColor = new Color(0.78f, 0.78f, 0.78f, 1f) }
            };

            _badgeStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 10,
                padding = new RectOffset(6, 6, 2, 2),
                normal = { textColor = Color.white }
            };

            _smallLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f, 1f) }
            };

            _listItemStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                padding = new RectOffset(12, 4, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                alignment = TextAnchor.MiddleLeft
            };

            _listItemDetailStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 9,
                padding = new RectOffset(0, 6, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color(0.6f, 0.6f, 0.6f, 1f) }
            };

            _earlyAccessListStyle = new GUIStyle(_listItemDetailStyle)
            {
                fontSize = 8,
                fontStyle = FontStyle.Bold,
                normal = { textColor = _updateColor }
            };

            _categoryStyle = new GUIStyle(EditorStyles.foldout)
            {
                fontSize = 9,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(14, 4, 3, 3),
                margin = new RectOffset(0, 0, 0, 0),
                normal = { textColor = new Color(0.48f, 0.48f, 0.48f, 1f) }
            };

            _categoryUpdateStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 8,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
                padding = new RectOffset(2, 2, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                normal = { textColor = _updateColor }
            };

            _detailTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                margin = new RectOffset(0, 0, 0, 4)
            };

            var notesColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            _releaseNotesStyle = new GUIStyle(EditorStyles.label)
            {
                wordWrap = true,
                fontSize = 11,
                richText = true,
                normal = { textColor = notesColor },
                focused = { textColor = notesColor },
                onFocused = { textColor = notesColor }
            };
        }

        private void RebuildSortedPackages()
        {
            _sortedPackages.Clear();
            _categories.Clear();

            if (_packages?.Packages == null)
                return;

            foreach (var package in _packages.Packages)
            {
                if (package.IsHidden)
                    continue;
                _sortedPackages.Add((package, FindLatestByChannel(package, "release"), FindLatestByChannel(package, "dev")));
            }

            _sortedPackages.Sort((a, b) => a.pkg.DisplayOrder.CompareTo(b.pkg.DisplayOrder));

            // Build category index
            var categoryMap = new Dictionary<string, int>();
            foreach (var item in _sortedPackages)
            {
                var cat = item.pkg.Category ?? "";
                if (!categoryMap.ContainsKey(cat))
                {
                    categoryMap[cat] = _categories.Count;
                    _categories.Add((cat, 0, 0));
                }
            }

            // Compute start index and count per category
            int idx = 0;
            foreach (var item in _sortedPackages)
            {
                var cat = item.pkg.Category ?? "";
                int ci = categoryMap[cat];
                var c = _categories[ci];
                if (c.count == 0)
                    _categories[ci] = (c.name, idx, 1);
                else
                    _categories[ci] = (c.name, c.startIndex, c.count + 1);
                idx++;
            }
        }

        private void OnGUI()
        {
            InitStyles();

            // Handle splitter drag FIRST, before any GUILayout controls can consume events
            HandleSplitterDrag(_cachedSplitterRect);

            DrawHeader();
            DrawSeparator();

            if (_isLoading)
            {
                EditorGUILayout.Space(40);
                DrawCenteredLabel("Loading packages...");
                return;
            }

            if (!string.IsNullOrEmpty(_errorMessage))
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.HelpBox(_errorMessage, MessageType.Error);
                EditorGUILayout.Space(4);
                if (GUILayout.Button("Retry", GUILayout.Height(24)))
                    LoadData();
                return;
            }

            if (_packages?.Packages == null || _packages.Packages.Length == 0)
            {
                EditorGUILayout.Space(40);
                DrawCenteredLabel("No packages available.");
                return;
            }

            RebuildSortedPackages();
            _updatableCount = RebuildCategoryUpdateCounts();
            ReconcileSelectionWithSearch();

            _splitWidth = Mathf.Clamp(_splitWidth, SplitMargin, position.width - SplitMargin);

            // Split view: left placeholder + splitter space + right detail (EditorGUILayout)
            EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));

            // Left panel: reserve space for the list (drawn with immediate-mode later)
            EditorGUILayout.BeginVertical(GUILayout.Width(_splitWidth));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndVertical();
            var listRect = GUILayoutUtility.GetLastRect();

            // Splitter space
            GUILayout.Space(SplitterWidth);

            // Right panel: detail using EditorGUILayout (gets proper Layout pass)
            DrawPackageDetail();

            EditorGUILayout.EndHorizontal();

            // Overlay immediate-mode list and splitter, using exact positions to avoid layout padding gaps
            if (Event.current.type != EventType.Layout)
            {
                var fullListRect = new Rect(0, listRect.y, _splitWidth, listRect.height);
                _cachedSplitterRect = new Rect(_splitWidth, listRect.y, SplitterWidth, listRect.height);
                DrawPackageList(fullListRect);
                DrawSplitter(_cachedSplitterRect);
            }

            _prevKeyboardControl = GUIUtility.keyboardControl;
        }

        private void DrawHeader()
        {
            var headerRect = GUILayoutUtility.GetRect(0, 42, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(headerRect, _headerBg);

            var logoRect = new Rect(headerRect.x + 10, headerRect.y + 7, 28, 28);
            if (_logo != null)
                GUI.DrawTexture(logoRect, _logo, ScaleMode.ScaleToFit);

            var labelRect = new Rect(logoRect.xMax + 8, headerRect.y + 4, 200, 20);
            var headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
            GUI.Label(labelRect, "PurrNet Packages", headerStyle);

            // Tier badge (only shown for paid memberships)
            if (_entitlements != null)
            {
                var tier = FormatTierName(_entitlements.Tier);
                if (tier != null)
                {
                    var tierRect = new Rect(labelRect.x, labelRect.yMax - 2, 100, 16);
                    GUI.Label(tierRect, tier, _smallLabelStyle);
                }
            }

            // Refresh button (rightmost)
            var refreshRect = new Rect(headerRect.xMax - 78, headerRect.y + 10, 68, 22);
            GUI.enabled = !_isLoading && !_isUpdatingAll && !HasActivePackageOperation();
            if (GUI.Button(refreshRect, "Refresh"))
            {
                PurrPackageManagerCache.Invalidate();
                _userProfile?.Refresh();
                LoadData();
            }
            GUI.enabled = true;

            float profileRight = refreshRect.x - 4;
            if (_userProfile?.Info?.IsAdmin == true)
            {
                var registeredPackage = FindCurrentRepositoryPackage();
                var repoRect = new Rect(refreshRect.x - 104, headerRect.y + 10, 100, 22);
                string repoLabel = registeredPackage != null
                    ? "Manage Repo"
                    : (_isRegisteringRepository ? BusyLabel("Registering") : "Register Repo");
                GUI.enabled = !_isRegisteringRepository;
                if (GUI.Button(repoRect, repoLabel))
                {
                    if (registeredPackage != null)
                        OpenPackageAdmin(registeredPackage.Id);
                    else
                        RegisterCurrentRepository();
                }
                GUI.enabled = true;
                profileRight = repoRect.x - 4;
            }

            // User profile (avatar + username + login/logout)
            var profileAnchor = new Rect(headerRect.x, headerRect.y + 10, profileRight - headerRect.x, 22);
            if (_userProfile != null)
            {
                float profileWidth = _userProfile.DrawProfileBar(profileAnchor);

                // Update All button (to the left of profile)
                if (_updatableCount > 0 || _isUpdatingAll)
                {
                    var updateLabel = _isUpdatingAll ? BusyLabel("Updating") : $"Update All ({_updatableCount})";
                    var updateRect = new Rect(profileAnchor.xMax - profileWidth - 104, headerRect.y + 10, 100, 22);
                    GUI.enabled = !_isLoading && !_isUpdatingAll && !HasActivePackageOperation();
                    GUI.color = _updateColor;
                    if (GUI.Button(updateRect, updateLabel))
                        UpdateAllPackages();
                    GUI.color = Color.white;
                    GUI.enabled = true;
                }
            }
        }

        private void DetectCurrentRepository()
        {
            _currentRepoOwner = null;
            _currentRepoName = null;
            _currentRepoDetectionError = null;

            try
            {
                string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "config --get remote.origin.url",
                    WorkingDirectory = projectRoot,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = new System.Diagnostics.Process { StartInfo = startInfo };
                if (!process.Start())
                {
                    _currentRepoDetectionError = "Could not start Git to inspect this repository.";
                    return;
                }

                if (!process.WaitForExit(3000))
                {
                    process.Kill();
                    _currentRepoDetectionError = "Git timed out while reading the origin remote.";
                    return;
                }

                string remote = process.StandardOutput.ReadToEnd().Trim();
                if (process.ExitCode != 0 || string.IsNullOrEmpty(remote))
                {
                    _currentRepoDetectionError = "This Unity project has no Git origin remote.";
                    return;
                }

                var match = Regex.Match(remote, @"github\.com[/:](?<owner>[^/\s]+?)/(?<repo>[^/#\s]+)", RegexOptions.IgnoreCase);
                if (!match.Success)
                {
                    _currentRepoDetectionError = $"The Git origin is not a GitHub repository:\n{remote}";
                    return;
                }

                _currentRepoOwner = match.Groups["owner"].Value;
                _currentRepoName = Regex.Replace(match.Groups["repo"].Value, @"\.git$", string.Empty, RegexOptions.IgnoreCase);
            }
            catch (Exception e)
            {
                _currentRepoDetectionError = $"Could not inspect the current Git repository:\n{e.Message}";
            }
        }

        private PackageInfo FindCurrentRepositoryPackage()
        {
            if (string.IsNullOrEmpty(_currentRepoOwner) || string.IsNullOrEmpty(_currentRepoName) || _packages?.Packages == null)
                return null;

            foreach (var package in _packages.Packages)
            {
                if (string.Equals(package.GithubOwner, _currentRepoOwner, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(package.GithubRepo, _currentRepoName, StringComparison.OrdinalIgnoreCase))
                    return package;
            }

            return null;
        }

        private async void RegisterCurrentRepository()
        {
            if (_isRegisteringRepository)
                return;

            if (string.IsNullOrEmpty(_currentRepoOwner) || string.IsNullOrEmpty(_currentRepoName))
            {
                EditorUtility.DisplayDialog("Repository Not Found",
                    _currentRepoDetectionError ?? "Could not identify this project's GitHub origin.", "Ok");
                return;
            }

            string apiKey = PurrPackageManagerAuth.GetApiKey();
            if (string.IsNullOrEmpty(apiKey))
            {
                EditorUtility.DisplayDialog("Login Required", "Log in before registering a package.", "Ok");
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "Register Current Repository",
                $"Register {_currentRepoOwner}/{_currentRepoName} as a PurrNet package?\n\n" +
                "It will start as Admin Only and Early Access so it is safe for internal testing. " +
                "The remote repository must contain a discoverable Unity package.json.",
                "Register",
                "Cancel");
            if (!confirmed)
                return;

            _isRegisteringRepository = true;
            Repaint();
            try
            {
                var registration = new PackageRegistrationRequest(_currentRepoOwner, _currentRepoName, _currentRepoName);
                var result = await PurrPackageManagerAPI.RegisterPackage(apiKey, registration);
                if (!result.Success)
                {
                    bool alreadyRegistered = result.Error?.IndexOf("already exists", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (alreadyRegistered && EditorUtility.DisplayDialog("Repository Already Registered",
                            $"{_currentRepoOwner}/{_currentRepoName} is already registered.", "Manage on Website", "Close"))
                    {
                        Application.OpenURL(PackageAdminUrl);
                    }
                    else if (!alreadyRegistered)
                    {
                        EditorUtility.DisplayDialog("Registration Failed", result.Error ?? "Unknown error", "Ok");
                    }
                    return;
                }

                PurrPackageManagerCache.Invalidate();
                LoadData();

                string packageId = result.Value?.Package?.Id;
                if (EditorUtility.DisplayDialog("Package Registered",
                        $"{_currentRepoOwner}/{_currentRepoName} is now registered as Admin Only and Early Access.",
                        "Manage on Website", "Done"))
                {
                    OpenPackageAdmin(packageId);
                }
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Registration Failed", e.Message, "Ok");
            }
            finally
            {
                _isRegisteringRepository = false;
                Repaint();
            }
        }

        private static void OpenPackageAdmin(string packageId)
        {
            string url = string.IsNullOrEmpty(packageId)
                ? PackageAdminUrl
                : $"{PackageAdminUrl}?edit={Uri.EscapeDataString(packageId)}";
            Application.OpenURL(url);
        }


        private void DrawPackageList(Rect areaRect)
        {
            EditorGUI.DrawRect(areaRect, _listBg);

            var searchBackgroundRect = new Rect(
                areaRect.x,
                areaRect.y,
                areaRect.width,
                SearchAreaHeight);
            EditorGUI.DrawRect(searchBackgroundRect, _categoryBg);

            var searchRect = new Rect(
                searchBackgroundRect.x + 5f,
                searchBackgroundRect.y + 4f,
                Mathf.Max(0f, searchBackgroundRect.width - 10f),
                18f);
            string nextSearchQuery = _searchField.OnGUI(searchRect, _searchQuery ?? string.Empty);
            if (!string.Equals(nextSearchQuery, _searchQuery, StringComparison.Ordinal))
            {
                _searchQuery = nextSearchQuery;
                _listScrollPosition = Vector2.zero;
                Repaint();
            }

            var listAreaRect = new Rect(
                areaRect.x,
                areaRect.y + SearchAreaHeight,
                areaRect.width,
                Mathf.Max(0f, areaRect.height - SearchAreaHeight));
            bool isSearching = HasSearchQuery();

            // Calculate total content height
            float totalHeight = 0;
            int visibleCategoryCount = 0;
            for (int c = 0; c < _categories.Count; c++)
            {
                var category = _categories[c];
                int visiblePackageCount = CountSearchMatches(category.startIndex, category.count);
                bool showStudios = ShouldShowStudiosEntry(category.name);
                if (visiblePackageCount == 0 && !showStudios)
                    continue;

                if (visibleCategoryCount > 0) totalHeight += CategoryGap;
                visibleCategoryCount++;
                totalHeight += CategoryHeaderHeight;
                if (isSearching || IsCategoryExpanded(category.name))
                {
                    totalHeight += (visiblePackageCount + (showStudios ? 1 : 0)) * ListItemHeight;
                }
            }

            if (visibleCategoryCount == 0)
            {
                var emptyRect = new Rect(
                    listAreaRect.x + 10f,
                    listAreaRect.y + 12f,
                    Mathf.Max(0f, listAreaRect.width - 20f),
                    18f);
                GUI.Label(emptyRect, "No matching packages.", _smallLabelStyle);
                return;
            }

            bool needsScroll = totalHeight > listAreaRect.height;
            var viewRect = new Rect(0, 0, listAreaRect.width - (needsScroll ? 13f : 0f), totalHeight);
            _listScrollPosition = GUI.BeginScrollView(listAreaRect, _listScrollPosition, viewRect);

            float y = 0;
            bool firstCategory = true;
            foreach (var (categoryName, startIndex, count) in _categories)
            {
                int visiblePackageCount = CountSearchMatches(startIndex, count);
                bool showStudios = ShouldShowStudiosEntry(categoryName);
                if (visiblePackageCount == 0 && !showStudios)
                    continue;

                // Gap between categories
                if (!firstCategory)
                    y += CategoryGap;
                firstCategory = false;

                // Category header
                var catLabel = string.IsNullOrEmpty(categoryName) ? "Other" : categoryName;
                var catRect = new Rect(0, y, viewRect.width, CategoryHeaderHeight);

                EditorGUI.DrawRect(catRect, _categoryBg);
                bool isExpanded;
                if (isSearching)
                {
                    isExpanded = true;
                    if (Event.current.type == EventType.Repaint)
                    {
                        _categoryStyle.Draw(
                            catRect,
                            new GUIContent(catLabel.ToUpperInvariant()),
                            catRect.Contains(Event.current.mousePosition),
                            false,
                            true,
                            false);
                    }
                }
                else
                {
                    isExpanded = IsCategoryExpanded(categoryName);
                    bool nextExpanded = GUI.Toggle(
                        catRect,
                        isExpanded,
                        catLabel.ToUpperInvariant(),
                        _categoryStyle);
                    if (nextExpanded != isExpanded)
                    {
                        SetCategoryExpanded(categoryName, nextExpanded);
                        isExpanded = nextExpanded;
                    }
                }

                if (_categoryUpdateCounts.TryGetValue(categoryName ?? string.Empty, out int categoryUpdateCount))
                    DrawCategoryUpdateIndicator(catRect, categoryUpdateCount);

                y += CategoryHeaderHeight;

                if (!isExpanded)
                    continue;

                // Package items in this category
                for (int i = startIndex; i < startIndex + count; i++)
                {
                    if (!PackageMatchesSearch(_sortedPackages[i].pkg))
                        continue;

                    var itemRect = new Rect(0, y, viewRect.width, ListItemHeight);
                    var entry = _sortedPackages[i];
                    DrawListItem(entry.pkg, entry.release, entry.dev, i, itemRect);
                    y += ListItemHeight;
                }

                // "PurrNet for Studios" entry at the end of the Core category
                if (showStudios)
                {
                    var studioRect = new Rect(0, y, viewRect.width, ListItemHeight);
                    DrawStudiosListItem(studioRect);
                    y += ListItemHeight;
                }
            }

            GUI.EndScrollView();
        }

        private void ReconcileSelectionWithSearch()
        {
            int nextIndex = _selectedIndex;

            if (nextIndex != StudiosEntryIndex &&
                (nextIndex < 0 || nextIndex >= _sortedPackages.Count))
            {
                nextIndex = -1;
            }

            if (HasSearchQuery())
            {
                bool selectionMatches = nextIndex == StudiosEntryIndex
                    ? MatchesStudiosSearch()
                    : nextIndex >= 0 && PackageMatchesSearch(_sortedPackages[nextIndex].pkg);
                if (!selectionMatches)
                    nextIndex = FindFirstSearchResult();
            }
            else if (nextIndex < 0 && _sortedPackages.Count > 0)
            {
                nextIndex = 0;
            }

            if (nextIndex == _selectedIndex)
                return;

            _selectedIndex = nextIndex;
            _releasePopupIndex = -1;
            _devPopupIndex = -1;
            _releasePopupTouched = false;
            _devPopupTouched = false;
        }

        private int FindFirstSearchResult()
        {
            for (int i = 0; i < _sortedPackages.Count; i++)
            {
                if (PackageMatchesSearch(_sortedPackages[i].pkg))
                    return i;
            }

            return MatchesStudiosSearch() ? StudiosEntryIndex : -1;
        }

        private int CountSearchMatches(int startIndex, int count)
        {
            int matches = 0;
            for (int i = startIndex; i < startIndex + count; i++)
            {
                if (PackageMatchesSearch(_sortedPackages[i].pkg))
                    matches++;
            }

            return matches;
        }

        private bool ShouldShowStudiosEntry(string categoryName)
        {
            return string.Equals(categoryName, "Core", StringComparison.OrdinalIgnoreCase) &&
                   MatchesStudiosSearch();
        }

        private bool HasSearchQuery()
        {
            return !string.IsNullOrWhiteSpace(_searchQuery);
        }

        private bool PackageMatchesSearch(PackageInfo package)
        {
            if (!HasSearchQuery())
                return true;

            string[] tokens = Regex.Split(_searchQuery.Trim(), @"\s+");
            foreach (string token in tokens)
            {
                if (ContainsSearchToken(package.DisplayName, token) ||
                    ContainsSearchToken(package.Id, token) ||
                    ContainsSearchToken(package.Slug, token) ||
                    ContainsSearchToken(package.UpmPackageName, token) ||
                    ContainsSearchToken(package.Category, token) ||
                    ContainsSearchToken(package.Description, token) ||
                    ContainsSearchToken(package.GithubOwner, token) ||
                    ContainsSearchToken(package.GithubRepo, token) ||
                    ContainsSearchToken(package.RequiredTier, token) ||
                    package.IsEarlyAccess && ContainsSearchToken("early access", token) ||
                    package.IsUserEditable && ContainsSearchToken("user editable", token))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private bool MatchesStudiosSearch()
        {
            if (!HasSearchQuery())
                return true;

            string[] tokens = Regex.Split(_searchQuery.Trim(), @"\s+");
            const string studiosSearchText = "PurrNet for Studios studio premium team source access";
            foreach (string token in tokens)
            {
                if (!ContainsSearchToken(studiosSearchText, token))
                    return false;
            }

            return true;
        }

        private static bool ContainsSearchToken(string value, string token)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void DrawCategoryUpdateIndicator(Rect categoryRect, int updateCount)
        {
            bool useCompactLabel = categoryRect.width < 150f;
            string label = useCompactLabel
                ? $"↑ {updateCount}"
                : updateCount == 1 ? "1 UPDATE" : $"{updateCount} UPDATES";
            string packageLabel = updateCount == 1 ? "package has" : "packages have";
            var content = new GUIContent(label,
                $"{updateCount} {packageLabel} an update available in this category.");

            float width = Mathf.Ceil(_categoryUpdateStyle.CalcSize(content).x);
            var labelRect = new Rect(
                categoryRect.xMax - width - 5f,
                categoryRect.y + 3f,
                width,
                CategoryHeaderHeight - 6f);

            // Cover any long category title beneath the right-aligned indicator.
            EditorGUI.DrawRect(new Rect(labelRect.x - 2f, categoryRect.y, width + 7f, categoryRect.height),
                _categoryBg);
            GUI.Label(labelRect, content, _categoryUpdateStyle);
        }

        private static bool IsCategoryExpanded(string categoryName)
        {
            return EditorPrefs.GetBool(CategoryFoldoutPreferencePrefix + (categoryName ?? string.Empty), true);
        }

        private static void SetCategoryExpanded(string categoryName, bool isExpanded)
        {
            EditorPrefs.SetBool(CategoryFoldoutPreferencePrefix + (categoryName ?? string.Empty), isExpanded);
        }

        private void DrawListItem(PackageInfo package, VersionInfo release, VersionInfo dev, int index, Rect itemRect)
        {
            bool isSelected = index == _selectedIndex;
            bool isInstalled = PurrPackageManagerInstaller.IsInstalled(package);
            bool isGitInstall = isInstalled && PurrPackageManagerInstaller.IsInstalledViaGit(package);
            var installedVersion = isInstalled ? PurrPackageManagerInstaller.GetInstalledVersion(package) : null;
            var updateTarget = isInstalled ? GetVersionUpdateTarget(package, installedVersion, release, dev) : null;
            bool hasUpdate = updateTarget != null;

            // Hover detection
            bool isHover = itemRect.Contains(Event.current.mousePosition);

            // Background
            Color tierColor = default;
            bool hasTierColor = !string.IsNullOrEmpty(package.RequiredTier)
                                && _tierColors.TryGetValue(package.RequiredTier, out tierColor);
            if (isSelected)
            {
                EditorGUI.DrawRect(itemRect, _selectedBg);
                EditorGUI.DrawRect(new Rect(itemRect.x, itemRect.y, 3, itemRect.height),
                    hasTierColor ? tierColor : _selectedAccent);
            }
            else if (hasTierColor)
            {
                var bg = new Color(tierColor.r, tierColor.g, tierColor.b, isHover ? 0.12f : 0.06f);
                EditorGUI.DrawRect(itemRect, bg);
                EditorGUI.DrawRect(new Rect(itemRect.x, itemRect.y, 2, itemRect.height),
                    new Color(tierColor.r, tierColor.g, tierColor.b, isHover ? 0.6f : 0.3f));
            }
            else if (isHover)
            {
                EditorGUI.DrawRect(itemRect, _hoverBg);
            }

            // Follow the channel already installed so the row, Update All count, and batch target agree.
            bool hasGitUpdate = package.IsExternal && isGitInstall
                                && TryGetExternalUpdateTarget(package, out _, out _);

            // Build right-side info text
            bool showEarlyAccess = package.IsEarlyAccess && package.HasAccess;
            string info;
            if (!package.HasAccess)
                info = "No access";
            else if (showEarlyAccess)
                info = "EARLY ACCESS";
            else if (package.IsExternal && isGitInstall)
                info = hasGitUpdate ? "update" : "installed";
            else if (hasUpdate)
                info = $"v{installedVersion} \u2192 v{updateTarget.Version}";
            else if (isInstalled && installedVersion != null)
                info = $"v{installedVersion}";
            else if (!string.IsNullOrEmpty(package.LatestVersion))
                info = $"v{package.LatestVersion}";
            else
                info = "";

            // Measure right-side text width
            var infoStyle = showEarlyAccess ? _earlyAccessListStyle : _listItemDetailStyle;
            float infoWidth = string.IsNullOrEmpty(info) ? 0 : infoStyle.CalcSize(new GUIContent(info)).x + 4;

            // Status dot
            bool showDot = hasUpdate || hasGitUpdate || isInstalled;
            float dotSpace = showDot ? 12 : 0;

            // Name (left) — drawn as pure text, no event handling
            float nameWidth = itemRect.width - infoWidth - dotSpace;
            var nameRect = new Rect(itemRect.x, itemRect.y, nameWidth, itemRect.height);
            if (Event.current.type == EventType.Repaint)
                _listItemStyle.Draw(nameRect, package.DisplayName, false, false, false, false);

            // Status dot (between name and info)
            if (showDot)
            {
                var dotColor = (hasUpdate || hasGitUpdate) ? _updateColor : _installedColor;
                float dotX = nameRect.xMax + 2;
                var dotRect = new Rect(dotX, itemRect.y + (itemRect.height - 6) / 2, 6, 6);
                EditorGUI.DrawRect(dotRect, dotColor);
            }

            // Info text (right-aligned) — drawn as pure text, no event handling
            if (infoWidth > 0 && Event.current.type == EventType.Repaint)
            {
                var infoRect = new Rect(itemRect.xMax - infoWidth, itemRect.y, infoWidth, itemRect.height);
                if (!package.HasAccess)
                {
                    var noAccessStyle = new GUIStyle(_listItemDetailStyle)
                    {
                        fontSize = 10,
                        fontStyle = FontStyle.Bold,
                        normal = { textColor = _noAccessColor }
                    };
                    noAccessStyle.Draw(infoRect, info, false, false, false, false);
                }
                else
                {
                    infoStyle.Draw(infoRect, info, false, false, false, false);
                }
            }

            // Click to select — at the end so nothing above can interfere
            if (Event.current.type == EventType.MouseDown && itemRect.Contains(Event.current.mousePosition))
            {
                _selectedIndex = index;
                _releasePopupIndex = -1;
                _devPopupIndex = -1;
                _releasePopupTouched = false;
                _devPopupTouched = false;
                GUI.FocusControl(null);
                Event.current.Use();
                Repaint();
            }

            // Repaint on hover for highlight
            if (isHover && Event.current.type == EventType.Repaint)
                Repaint();
        }

        private static readonly Color _studiosAccent = new Color(0.85f, 0.65f, 0.3f, 1f);
        private static readonly Color _studiosBg = new Color(0.85f, 0.65f, 0.3f, 0.06f);
        private static readonly Color _studiosHoverBg = new Color(0.85f, 0.65f, 0.3f, 0.12f);

        private void DrawStudiosListItem(Rect itemRect)
        {
            bool isSelected = _selectedIndex == StudiosEntryIndex;
            bool isHover = itemRect.Contains(Event.current.mousePosition);

            if (isSelected)
            {
                EditorGUI.DrawRect(itemRect, _selectedBg);
                EditorGUI.DrawRect(new Rect(itemRect.x, itemRect.y, 2, itemRect.height), _studiosAccent);
            }
            else
            {
                EditorGUI.DrawRect(itemRect, isHover ? _studiosHoverBg : _studiosBg);
                EditorGUI.DrawRect(new Rect(itemRect.x, itemRect.y, 2, itemRect.height),
                    new Color(_studiosAccent.r, _studiosAccent.g, _studiosAccent.b, isHover ? 0.6f : 0.3f));
            }

            if (Event.current.type == EventType.Repaint)
            {
                var nameRect = new Rect(itemRect.x, itemRect.y, itemRect.width, itemRect.height);
                _listItemStyle.Draw(nameRect, "PurrNet for Studios", false, false, false, false);
            }

            if (Event.current.type == EventType.MouseDown && itemRect.Contains(Event.current.mousePosition))
            {
                _selectedIndex = StudiosEntryIndex;
                _releasePopupIndex = -1;
                _devPopupIndex = -1;
                _releasePopupTouched = false;
                _devPopupTouched = false;
                GUI.FocusControl(null);
                Event.current.Use();
                Repaint();
            }

            if (isHover && Event.current.type == EventType.Repaint)
                Repaint();
        }

        private void DrawStudiosDetail()
        {
            _detailScrollPosition = EditorGUILayout.BeginScrollView(_detailScrollPosition,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            EditorGUILayout.Space(8);
            GUILayout.Space(4);

            // Title row
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            GUILayout.Label("PurrNet for Studios", _detailTitleStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();

            // Description
            EditorGUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            EditorGUILayout.LabelField(
                "Professional networking solutions with dedicated support, custom integrations, " +
                "and consulting services designed to help your studio succeed at scale.",
                _descStyle);
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();

            // Action buttons
            EditorGUILayout.Space(12);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            GUI.color = _accentColor;
            if (GUILayout.Button("Learn More", GUILayout.Height(28)))
                Application.OpenURL("https://purrnet.dev/studios");
            GUILayout.Space(4);
            if (GUILayout.Button("Contact Us", GUILayout.Height(28)))
                Application.OpenURL("mailto:martin@pebblesgames.com");
            GUI.color = Color.white;
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();

            // Enterprise features
            EditorGUILayout.Space(12);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            EditorGUILayout.BeginVertical();
            GUILayout.Label("Enterprise Features", _detailTitleStyle);
            EditorGUILayout.Space(4);
            GUILayout.Label("\u2022  Priority support with direct access to the engineering team", _smallLabelStyle);
            GUILayout.Label("\u2022  Hands-on project access for immediate troubleshooting and fixes", _smallLabelStyle);
            GUILayout.Label("\u2022  Custom integrations tailored to your infrastructure and workflows", _smallLabelStyle);
            GUILayout.Label("\u2022  Performance optimization tuned to your scale requirements", _smallLabelStyle);
            EditorGUILayout.EndVertical();
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();

            // Singleplayer to multiplayer
            EditorGUILayout.Space(12);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            EditorGUILayout.BeginVertical();
            GUILayout.Label("Singleplayer to Multiplayer", _detailTitleStyle);
            EditorGUILayout.Space(4);
            GUILayout.Label("\u2022  Full conversion of existing singleplayer projects to multiplayer", _smallLabelStyle);
            GUILayout.Label("\u2022  Custom tooling built around your game's specific needs", _smallLabelStyle);
            GUILayout.Label("\u2022  State synchronization, lobby systems, and matchmaking integration", _smallLabelStyle);
            GUILayout.Label("\u2022  Minimal disruption to your existing codebase and workflows", _smallLabelStyle);
            EditorGUILayout.EndVertical();
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();

            // Consulting services
            EditorGUILayout.Space(12);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            EditorGUILayout.BeginVertical();
            GUILayout.Label("Consulting Services", _detailTitleStyle);
            EditorGUILayout.Space(4);
            GUILayout.Label("\u2022  Architecture review and optimization recommendations", _smallLabelStyle);
            GUILayout.Label("\u2022  Migration planning from other networking solutions", _smallLabelStyle);
            GUILayout.Label("\u2022  Performance auditing to identify bottlenecks", _smallLabelStyle);
            GUILayout.Label("\u2022  Custom development for bespoke features and integrations", _smallLabelStyle);
            EditorGUILayout.EndVertical();
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(12);
            EditorGUILayout.EndScrollView();
        }

        private void DrawPackageDetail()
        {
            if (_selectedIndex == StudiosEntryIndex)
            {
                DrawStudiosDetail();
                return;
            }

            _detailScrollPosition = EditorGUILayout.BeginScrollView(_detailScrollPosition,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            if (_selectedIndex < 0 || _selectedIndex >= _sortedPackages.Count)
            {
                EditorGUILayout.Space(40);
                DrawCenteredLabel("Select a package to view details.");
                EditorGUILayout.EndScrollView();
                return;
            }

            var (package, release, dev) = _sortedPackages[_selectedIndex];
            var installedVersion = PurrPackageManagerInstaller.GetInstalledVersion(package);
            bool isInstalled = PurrPackageManagerInstaller.IsInstalled(package);
            bool isGitInstall = isInstalled && PurrPackageManagerInstaller.IsInstalledViaGit(package);
            var updateTarget = isInstalled ? GetVersionUpdateTarget(package, installedVersion, release, dev) : null;
            bool hasUpdate = updateTarget != null;

            // Follow the channel already installed so the detail badge matches Update All.
            string gitInstalledHash = null;
            string gitInstalledChannel = null;
            bool hasGitUpdate = false;
            if (package.IsExternal && isGitInstall)
            {
                gitInstalledHash = PurrPackageManagerInstaller.GetInstalledCommitHash(package);
                gitInstalledChannel = PurrPackageManagerInstaller.GetInstalledGitChannel(package);
                hasGitUpdate = TryGetExternalUpdateTarget(package, out _, out _);
            }

            EditorGUILayout.Space(8);
            GUILayout.Space(4);

            // Title row: name + badges
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            GUILayout.Label(package.DisplayName, _detailTitleStyle);
            GUILayout.FlexibleSpace();

            if (package.IsEarlyAccess)
                DrawBadge("EARLY ACCESS", _updateColor);

            if (package.IsUserEditable)
                DrawBadge("USER EDITABLE", _accentColor);

            if (package.IsExternal && isGitInstall)
            {
                if (hasGitUpdate)
                {
                    DrawBadge("UPDATE", _updateColor);
                    DrawBadge("INSTALLED", _installedColor);
                }
                else
                {
                    DrawBadge("INSTALLED", _installedColor);
                }
            }
            else if (package.Frozen)
            {
                DrawBadge("FROZEN", _frozenColor);
            }
            else if (hasUpdate)
            {
                DrawBadge("UPDATE", _updateColor);
                DrawBadge($"v{installedVersion}", _installedColor);
            }
            else if (installedVersion != null)
            {
                DrawBadge($"v{installedVersion}", _installedColor);
            }
            else if (!string.IsNullOrEmpty(package.LatestVersion))
            {
                DrawBadge($"v{package.LatestVersion}", _accentColor);
            }

            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();

            // Description
            if (!string.IsNullOrEmpty(package.Description))
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(8);
                EditorGUILayout.LabelField(package.Description, _descStyle);
                GUILayout.Space(8);
                EditorGUILayout.EndHorizontal();
            }

            // Info section
            EditorGUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            EditorGUILayout.BeginVertical();

            string tierName = FormatTierName(package.RequiredTier);
            if (!string.IsNullOrEmpty(tierName))
                GUILayout.Label($"Tier: {tierName}", _smallLabelStyle);

            if (package.IsUserEditable)
                GUILayout.Label("Uses Unity's interactive importer under Assets; update this package individually",
                    _smallLabelStyle);

            if (!string.IsNullOrEmpty(package.Slug))
            {
                string packageUrl = PackageWebsiteBaseUrl + Uri.EscapeDataString(package.Slug);
                if (GUILayout.Button(packageUrl, EditorStyles.linkLabel, GUILayout.ExpandWidth(false)))
                    Application.OpenURL(packageUrl);
                EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);
            }

            if (package.IsExternal && isGitInstall)
            {
                GUILayout.Label("Installed via git", _smallLabelStyle);
                if (installedVersion != null && installedVersion != "git")
                    GUILayout.Label($"Version: v{installedVersion}", _smallLabelStyle);
                if (gitInstalledHash != null)
                    GUILayout.Label($"Commit: {gitInstalledHash.Substring(0, Math.Min(8, gitInstalledHash.Length))}", _smallLabelStyle);
                if (hasGitUpdate)
                    GUILayout.Label("Update available", _smallLabelStyle);
            }
            else
            {
                if (isInstalled && installedVersion != null)
                    GUILayout.Label($"Installed: v{installedVersion}", _smallLabelStyle);

                if (!string.IsNullOrEmpty(package.LatestVersion))
                    GUILayout.Label($"Latest: v{package.LatestVersion}", _smallLabelStyle);
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();

            DrawDependencies(package);

            // Frozen notice (non-external only)
            if (!package.IsExternal && package.Frozen)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(8);
                EditorGUILayout.BeginVertical();

                string frozenMsg = !string.IsNullOrEmpty(package.EntitledVersion)
                    ? $"Access limited to v{package.EntitledVersion} and below. Resubscribe to unlock v{package.LatestVersion}."
                    : "Your access to this package is limited. Resubscribe to unlock the latest versions.";
                EditorGUILayout.HelpBox(frozenMsg, MessageType.Warning);

                GUI.color = _accentColor;
                if (GUILayout.Button("Resubscribe", GUILayout.Height(24)))
                    Application.OpenURL("https://purrnet.dev");
                GUI.color = Color.white;

                EditorGUILayout.EndVertical();
                GUILayout.Space(8);
                EditorGUILayout.EndHorizontal();
            }

            // No access
            if (!package.HasAccess)
            {
                EditorGUILayout.Space(12);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(8);
                GUI.color = _accentColor;
                if (GUILayout.Button("Get Access", GUILayout.Height(28)))
                    Application.OpenURL("https://purrnet.dev/membership");
                GUI.color = Color.white;
                GUILayout.Space(8);
                EditorGUILayout.EndHorizontal();

                if (isInstalled)
                {
                    EditorGUILayout.Space(8);
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(8);
                    GUI.color = _frozenColor;
                    GUI.enabled = CanStartPackageOperation();
                    if (GUILayout.Button("Remove Package", GUILayout.Height(24)))
                        RemovePackage(package);
                    GUI.enabled = true;
                    GUI.color = Color.white;
                    GUILayout.Space(8);
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.EndScrollView();
                return;
            }

            // Action buttons
            EditorGUILayout.Space(12);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            EditorGUILayout.BeginVertical();

            if (package.IsExternal)
            {
                // External packages: git URL install buttons
                EditorGUILayout.BeginHorizontal();
                DrawExternalInstallButton(package, "Release", package.GitInstallUrlRelease,
                    isInstalled, gitInstalledHash, gitInstalledChannel, package.LatestCommitRelease, _installedColor);
                GUILayout.Space(4);
                DrawExternalInstallButton(package, "Dev", package.GitInstallUrlDev,
                    isInstalled, gitInstalledHash, gitInstalledChannel, package.LatestCommitDev, _accentColor);
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                // Non-external packages: standard version-based buttons
                // Works the same regardless of whether currently installed via git or tgz.
                EditorGUILayout.BeginHorizontal();
                DrawInstallButton(package, release, "Release", isInstalled, installedVersion, _installedColor);
                GUILayout.Space(4);
                DrawInstallButton(package, dev, "Dev", isInstalled, installedVersion, _accentColor);
                EditorGUILayout.EndHorizontal();

                // Version history dropdowns — split by channel, capped at 20 each
                if (package.Versions != null && package.Versions.Length > 0)
                {
                    var releaseVersions = new List<VersionInfo>();
                    var devVersions = new List<VersionInfo>();

                    foreach (var v in package.Versions)
                    {
                        // Always keep the installed version in its channel list, even past
                        // the 20-item cap — otherwise the dropdown can't select it.
                        bool isInstalledVersion = isInstalled && v.Version == installedVersion;
                        if (string.Equals(v.Channel, "release", StringComparison.OrdinalIgnoreCase))
                        {
                            if (releaseVersions.Count < 20 || isInstalledVersion) releaseVersions.Add(v);
                        }
                        else
                        {
                            if (devVersions.Count < 20 || isInstalledVersion) devVersions.Add(v);
                        }
                    }

                    EditorGUILayout.Space(8);
                    DrawVersionDropdown("Release", releaseVersions, ref _releasePopupIndex, ref _releasePopupTouched,
                        isInstalled, installedVersion, package, _installedColor);
                    EditorGUILayout.Space(4);
                    DrawVersionDropdown("Dev", devVersions, ref _devPopupIndex, ref _devPopupTouched,
                        isInstalled, installedVersion, package, _accentColor);
                }
            }

            // Remove button
            if (isInstalled)
            {
                EditorGUILayout.Space(8);
                GUI.color = _frozenColor;
                GUI.enabled = CanStartPackageOperation();
                if (GUILayout.Button("Remove Package", GUILayout.Height(24)))
                    RemovePackage(package);
                GUI.enabled = true;
                GUI.color = Color.white;
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();

            // Changelog (non-external only)
            if (!package.IsExternal && package.Versions != null && package.Versions.Length > 0)
            {
                // Collect relevant versions:
                // - Not installed: just the latest version
                // - Installed: only versions newer than the installed one
                var relevantVersions = new List<VersionInfo>();
                if (!isInstalled)
                {
                    // Show the latest version that has release notes
                    foreach (var v in package.Versions)
                    {
                        if (!string.IsNullOrEmpty(v.ReleaseNotes))
                        {
                            relevantVersions.Add(v);
                            break;
                        }
                    }
                }
                else
                {
                    // Versions array is newest-first; collect until we hit the installed version
                    foreach (var v in package.Versions)
                    {
                        if (v.Version == installedVersion)
                            break;
                        if (!string.IsNullOrEmpty(v.ReleaseNotes))
                            relevantVersions.Add(v);
                    }
                }

                if (relevantVersions.Count > 0)
                {
                    EditorGUILayout.Space(12);
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(8);
                    EditorGUILayout.BeginVertical();
                    DrawSeparator();
                    EditorGUILayout.Space(4);

                    var ptitle = isInstalled && hasUpdate
                        ? $"What's New ({relevantVersions.Count} update{(relevantVersions.Count > 1 ? "s" : "")})"
                        : "Release Notes";
                    GUILayout.Label(ptitle, _detailTitleStyle);
                    EditorGUILayout.Space(4);

                    foreach (var v in relevantVersions)
                    {
                        DrawReleaseNotesText(v.ReleaseNotes);
                        EditorGUILayout.Space(8);
                    }

                    EditorGUILayout.EndVertical();
                    GUILayout.Space(8);
                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.Space(12);
            EditorGUILayout.EndScrollView();
        }

        private void DrawDependencies(PackageInfo package)
        {
            if (package.DependencyIds == null || package.DependencyIds.Length == 0)
                return;

            EditorGUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            EditorGUILayout.BeginVertical();

            GUILayout.Label($"Dependencies ({package.DependencyIds.Length})", EditorStyles.boldLabel);

            foreach (var dependencyId in package.DependencyIds)
            {
                var dependency = FindPackageById(dependencyId);
                string dependencyName = dependency?.DisplayName ?? dependencyId;
                string status;
                Color statusColor;

                if (dependency == null)
                {
                    status = "Unavailable";
                    statusColor = _frozenColor;
                }
                else if (PurrPackageManagerInstaller.IsInstalled(dependency))
                {
                    status = "Installed";
                    statusColor = _installedColor;
                }
                else if (!dependency.HasAccess)
                {
                    status = "No access";
                    statusColor = _noAccessColor;
                }
                else
                {
                    status = "Will install";
                    statusColor = _updateColor;
                }

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label($"\u2022  {dependencyName}", _smallLabelStyle);
                GUILayout.FlexibleSpace();

                var statusStyle = new GUIStyle(_listItemDetailStyle)
                {
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = statusColor }
                };
                GUILayout.Label(status, statusStyle, GUILayout.ExpandWidth(false));
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();
        }

        private PackageInfo FindPackageById(string packageId)
        {
            if (string.IsNullOrEmpty(packageId) || _packages?.Packages == null)
                return null;

            foreach (var package in _packages.Packages)
            {
                if (string.Equals(package.Id, packageId, StringComparison.OrdinalIgnoreCase))
                    return package;
            }

            return null;
        }

        private void DrawVersionDropdown(string channelLabel, List<VersionInfo> versions,
            ref int popupIndex, ref bool popupTouched, bool isInstalled, string installedVersion,
            PackageInfo package, Color color)
        {
            if (versions.Count == 0)
                return;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(channelLabel, EditorStyles.miniLabel, GUILayout.Width(48));

            var labels = new string[versions.Count];
            for (int i = 0; i < versions.Count; i++)
            {
                labels[i] = "v" + versions[i].Version;
                if (isInstalled && installedVersion == versions[i].Version)
                    labels[i] += " (installed)";
            }

            // Track the installed version until the user explicitly picks something else.
            // (popupTouched latches in that case so async installed-version refreshes don't snap it back.)
            if (!popupTouched && isInstalled && installedVersion != null)
            {
                for (int i = 0; i < versions.Count; i++)
                {
                    if (versions[i].Version == installedVersion)
                    {
                        popupIndex = i;
                        break;
                    }
                }
            }
            popupIndex = Mathf.Clamp(popupIndex, 0, labels.Length - 1);

            GUI.enabled = CanStartPackageOperation();
            int newIndex = EditorGUILayout.Popup(popupIndex, labels, GUILayout.Height(20));
            GUI.enabled = true;
            if (newIndex != popupIndex)
                popupTouched = true;
            popupIndex = newIndex;

            var selected = versions[popupIndex];
            bool isSelectedInstalled = isInstalled && installedVersion == selected.Version;
            var operationKey = GetPackageOperationKey(package, selected);
            bool isActive = IsPackageOperationActive(operationKey);
            string importLabel = package.IsUserEditable ? "Import" : "Install";
            string importingLabel = package.IsUserEditable ? "Importing" : "Installing";
            var buttonLabel = isSelectedInstalled
                ? "Installed"
                : isActive
                    ? BusyLabel(importingLabel)
                    : importLabel;

            GUI.enabled = !isSelectedInstalled && !isActive && CanStartPackageOperation();
            GUI.color = color;
            if (GUILayout.Button(buttonLabel, GUILayout.Width(66), GUILayout.Height(20)))
                InstallPackage(package, selected, operationKey);
            GUI.color = Color.white;
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
        }

        private void DrawInstallButton(PackageInfo package, VersionInfo version, string channelLabel,
            bool isInstalled, string installedVersion, Color buttonColor)
        {
            if (version == null)
            {
                GUI.enabled = false;
                GUILayout.Button($"No {channelLabel} Version", GUILayout.Height(26));
                GUI.enabled = true;
                return;
            }

            bool upToDate = isInstalled && installedVersion == version.Version;

            if (upToDate)
            {
                GUI.enabled = false;
                GUILayout.Button($"{channelLabel} v{version.Version} (installed)", GUILayout.Height(26));
                GUI.enabled = true;
            }
            else
            {
                GUI.color = buttonColor;
                string label;
                string activeLabel;
                if (!isInstalled)
                {
                    label = package.IsUserEditable
                        ? $"Import {channelLabel} v{version.Version}"
                        : $"Install {channelLabel} v{version.Version}";
                    activeLabel = package.IsUserEditable ? "Importing" : "Installing";
                }
                else if (IsInstalledOnChannel(package, version.Channel, installedVersion))
                {
                    label = $"Update to {channelLabel} v{version.Version}";
                    activeLabel = "Updating";
                }
                else
                {
                    label = $"Switch to {channelLabel} v{version.Version}";
                    activeLabel = "Switching";
                }
                var operationKey = GetPackageOperationKey(package, version);
                bool isActive = IsPackageOperationActive(operationKey);
                GUI.enabled = !isActive && CanStartPackageOperation();
                if (GUILayout.Button(isActive ? BusyLabel(activeLabel) : label, GUILayout.Height(26)))
                    InstallPackage(package, version, operationKey);
                GUI.enabled = true;
                GUI.color = Color.white;
            }
        }

        private void DrawExternalInstallButton(PackageInfo package, string channelLabel, string gitUrl,
            bool isInstalled, string installedHash, string installedChannel, string latestCommit, Color buttonColor)
        {
            if (string.IsNullOrEmpty(gitUrl))
            {
                GUI.enabled = false;
                GUILayout.Button($"No {channelLabel} Version", GUILayout.Height(26));
                GUI.enabled = true;
                return;
            }

            // Is this the channel/URL the package is currently installed from?
            bool isThisChannel = isInstalled
                && string.Equals(installedChannel, channelLabel, StringComparison.OrdinalIgnoreCase);

            if (isThisChannel)
            {
                bool hasNewerCommit = !string.IsNullOrEmpty(latestCommit)
                                      && !string.IsNullOrEmpty(installedHash)
                                      && !HashesMatch(installedHash, latestCommit);
                if (hasNewerCommit)
                {
                    var operationKey = GetPackageOperationKey(package, channelLabel);
                    bool isActive = IsPackageOperationActive(operationKey);
                    GUI.color = _updateColor;
                    GUI.enabled = !isActive && CanStartPackageOperation();
                    if (GUILayout.Button(isActive ? BusyLabel("Updating") : $"Update {channelLabel}", GUILayout.Height(26)))
                        InstallExternalPackage(package, gitUrl, operationKey);
                    GUI.enabled = true;
                    GUI.color = Color.white;
                }
                else
                {
                    GUI.enabled = false;
                    GUILayout.Button($"{channelLabel} (installed)", GUILayout.Height(26));
                    GUI.enabled = true;
                }
                return;
            }

            // Not installed at all, or installed from the other channel.
            var externalOperationKey = GetPackageOperationKey(package, channelLabel);
            bool isExternalActive = IsPackageOperationActive(externalOperationKey);
            var externalLabel = isInstalled ? $"Switch to {channelLabel}" : $"Install {channelLabel}";
            var externalBusyLabel = isInstalled ? "Switching" : "Installing";
            GUI.color = buttonColor;
            GUI.enabled = !isExternalActive && CanStartPackageOperation();
            if (GUILayout.Button(isExternalActive ? BusyLabel(externalBusyLabel) : externalLabel, GUILayout.Height(26)))
                InstallExternalPackage(package, gitUrl, externalOperationKey);
            GUI.enabled = true;
            GUI.color = Color.white;
        }

        // Markdown→rich-text is regex-heavy and the detail panel re-renders every OnGUI repaint;
        // the source notes are immutable, so memoize the rendered output.
        private static readonly Dictionary<string, string> _renderedNotesCache = new();

        private void DrawReleaseNotesText(string notes)
        {
            if (!_renderedNotesCache.TryGetValue(notes ?? "", out var rendered))
            {
                rendered = MarkdownToRichText(notes);
                _renderedNotesCache[notes ?? ""] = rendered;
            }
            var content = new GUIContent(rendered);
            var width = EditorGUIUtility.currentViewWidth - 40;
            var height = _releaseNotesStyle.CalcHeight(content, width);
            var rect = GUILayoutUtility.GetRect(content, _releaseNotesStyle, GUILayout.Height(height));
            EditorGUI.SelectableLabel(rect, rendered, _releaseNotesStyle);

            // Clear the select-all that happens on first focus
            int kb = GUIUtility.keyboardControl;
            if (kb != 0 && kb != _prevKeyboardControl)
            {
                var te = GUIUtility.GetStateObject(typeof(TextEditor), kb) as TextEditor;
                if (te != null)
                    te.selectIndex = te.cursorIndex;
            }
        }

        private void DrawBadge(string text, Color color)
        {
            var rect = GUILayoutUtility.GetRect(new GUIContent(text), _badgeStyle);
            rect.height = 18;
            EditorGUI.DrawRect(rect, new Color(color.r, color.g, color.b, 0.25f));
            var prevColor = GUI.color;
            GUI.color = color;
            GUI.Label(rect, text, _badgeStyle);
            GUI.color = prevColor;
        }

        private void DrawSeparator()
        {
            var rect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, _separatorColor);
        }

        private void DrawSplitter(Rect rect)
        {
            EditorGUI.DrawRect(rect, _separatorColor);

            // Draw grip dots in the center to hint it's draggable
            float centerX = rect.x + rect.width / 2f;
            float centerY = rect.y + rect.height / 2f;
            var dotColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            for (int i = -2; i <= 2; i++)
            {
                var dotRect = new Rect(centerX - 1, centerY + i * 5, 2, 2);
                EditorGUI.DrawRect(dotRect, dotColor);
            }

            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);
        }

        private void HandleSplitterDrag(Rect splitterRect)
        {
            if (splitterRect.width < 1)
                return;

            var evt = Event.current;

            switch (evt.type)
            {
                case EventType.MouseDown:
                    if (splitterRect.Contains(evt.mousePosition))
                    {
                        _isDraggingSplitter = true;
                        evt.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (_isDraggingSplitter)
                    {
                        _splitWidth = evt.mousePosition.x;
                        _splitWidth = Mathf.Clamp(_splitWidth, SplitMargin, position.width - SplitMargin);
                        evt.Use();
                        Repaint();
                    }
                    break;

                case EventType.MouseUp:
                    if (_isDraggingSplitter)
                    {
                        _isDraggingSplitter = false;
                        evt.Use();
                    }
                    break;
            }
        }

        private static void DrawCenteredLabel(string text)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label(text, EditorStyles.centeredGreyMiniLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private static bool IsInstalledOnChannel(PackageInfo package, string channel, string installedVersion)
        {
            if (package.Versions == null || installedVersion == null)
                return false;

            foreach (var v in package.Versions)
            {
                if (v.Version == installedVersion)
                    return string.Equals(v.Channel, channel, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        /// <summary>
        /// The version this package would update to given what's installed: the latest version in the
        /// channel it was installed from. A release install is NOT "outdated" just because a newer dev
        /// preview exists, so we only offer a dev update when the installed version is itself on dev.
        /// Returns null when not installed via a known version, or already up to date.
        /// </summary>
        private static VersionInfo GetVersionUpdateTarget(PackageInfo package, string installedVersion,
            VersionInfo release, VersionInfo dev)
        {
            if (string.IsNullOrEmpty(installedVersion) || installedVersion == "git")
                return null;

            var target = IsInstalledOnChannel(package, "dev", installedVersion) ? dev : (release ?? dev);
            if (target == null || target.Version == installedVersion)
                return null;
            return target;
        }

        private static bool HashesMatch(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                return false;
            int minLen = Math.Min(a.Length, b.Length);
            return string.Compare(a, 0, b, 0, minLen, StringComparison.OrdinalIgnoreCase) == 0;
        }

        private static VersionInfo FindLatestByChannel(PackageInfo package, string channel)
        {
            if (package.Versions == null)
                return null;

            foreach (var v in package.Versions)
            {
                if (string.Equals(v.Channel, channel, StringComparison.OrdinalIgnoreCase))
                    return v;
            }

            return null;
        }

        private static string MarkdownToRichText(string markdown)
        {
            if (string.IsNullOrEmpty(markdown))
                return markdown;

            var sb = new StringBuilder();
            var lines = markdown.Split('\n');
            bool lastWasBlank = false;

            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd('\r');

                // Collapse consecutive blank lines into one
                if (string.IsNullOrWhiteSpace(line))
                {
                    if (lastWasBlank) continue;
                    lastWasBlank = true;
                    sb.AppendLine();
                    continue;
                }
                lastWasBlank = false;

                // Headers
                if (line.StartsWith("### "))
                {
                    sb.AppendLine($"<b>{ProcessInline(line.Substring(4))}</b>");
                    continue;
                }
                if (line.StartsWith("## "))
                {
                    sb.AppendLine($"<size=13><b>{ProcessInline(line.Substring(3))}</b></size>");
                    continue;
                }
                if (line.StartsWith("# "))
                {
                    sb.AppendLine($"<size=15><b>{ProcessInline(line.Substring(2))}</b></size>");
                    continue;
                }

                // Unordered list items
                if (line.StartsWith("- ") || line.StartsWith("* "))
                {
                    sb.AppendLine($"  \u2022 {ProcessInline(line.Substring(2))}");
                    continue;
                }

                // Horizontal rules
                var trimmed = line.Trim();
                if (trimmed == "---" || trimmed == "***" || trimmed == "___")
                {
                    sb.AppendLine("\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500");
                    continue;
                }

                sb.AppendLine(ProcessInline(line));
            }

            return sb.ToString().TrimEnd();
        }

        private static string ProcessInline(string text)
        {
            // Links [text](url) → colored text
            text = Regex.Replace(text, @"\[([^\]]+)\]\([^\)]+\)", "<color=#66aaff>$1</color>");

            // Inline code `text`
            text = Regex.Replace(text, @"`([^`]+)`", "<color=#88cccc>$1</color>");

            // Bold **text** or __text__
            text = Regex.Replace(text, @"\*\*(.+?)\*\*", "<b>$1</b>");
            text = Regex.Replace(text, @"__(.+?)__", "<b>$1</b>");

            // Italic *text* or _text_
            text = Regex.Replace(text, @"(?<!\*)\*(.+?)\*(?!\*)", "<i>$1</i>");
            text = Regex.Replace(text, @"(?<!_)_(.+?)_(?!_)", "<i>$1</i>");

            return text;
        }

        private int RebuildCategoryUpdateCounts()
        {
            _categoryUpdateCounts.Clear();
            int totalCount = 0;

            foreach (var (categoryName, startIndex, count) in _categories)
            {
                int categoryCount = 0;
                for (int i = startIndex; i < startIndex + count; i++)
                {
                    var (pkg, release, dev) = _sortedPackages[i];
                    if (HasAvailableUpdate(pkg, release, dev))
                        categoryCount++;
                }

                if (categoryCount == 0)
                    continue;

                _categoryUpdateCounts[categoryName ?? string.Empty] = categoryCount;
                totalCount += categoryCount;
            }

            // The button also counts hidden updates and missing dependency installs that have no row.
            return _packages?.Packages == null ? totalCount : CollectUpdatablePackages().Count;
        }

        private static bool HasAvailableUpdate(PackageInfo pkg, VersionInfo release, VersionInfo dev)
        {
            // User-editable packages open Unity's interactive importer and may replace selected
            // project assets. They must be reviewed and updated individually, never unattended.
            if (!pkg.HasAccess || pkg.Frozen || pkg.IsUserEditable
                || !PurrPackageManagerInstaller.IsInstalled(pkg))
                return false;

            bool isGitInstall = PurrPackageManagerInstaller.IsInstalledViaGit(pkg);
            if (pkg.IsExternal && isGitInstall)
                return TryGetExternalUpdateTarget(pkg, out _, out _);

            var installedVersion = PurrPackageManagerInstaller.GetInstalledVersion(pkg);
            return GetVersionUpdateTarget(pkg, installedVersion, release, dev) != null;
        }

        private static bool TryGetExternalUpdateTarget(PackageInfo package, out string gitUrl,
            out string expectedCommit)
        {
            gitUrl = null;
            expectedCommit = null;

            var installedHash = PurrPackageManagerInstaller.GetInstalledCommitHash(package);
            if (string.IsNullOrEmpty(installedHash))
                return false;

            var channel = PurrPackageManagerInstaller.GetInstalledGitChannel(package);
            if (string.Equals(channel, "dev", StringComparison.OrdinalIgnoreCase))
            {
                gitUrl = package.GitInstallUrlDev;
                expectedCommit = package.LatestCommitDev;
            }
            else if (string.Equals(channel, "release", StringComparison.OrdinalIgnoreCase))
            {
                gitUrl = package.GitInstallUrlRelease;
                expectedCommit = package.LatestCommitRelease;
            }
            else
            {
                // An older/manual pin can be ambiguous when both channels use the same repository.
                // Leave it alone until the user explicitly chooses a channel.
                return false;
            }

            return !string.IsNullOrEmpty(gitUrl)
                   && !string.IsNullOrEmpty(expectedCommit)
                   && !HashesMatch(installedHash, expectedCommit);
        }

        private List<(PackageInfo pkg, VersionInfo version, string gitUrl, string expectedCommit)>
            CollectUpdatablePackages()
        {
            var updates = new List<(
                PackageInfo pkg, VersionInfo version, string gitUrl, string expectedCommit)>();

            foreach (var pkg in _packages.Packages)
            {
                if (!pkg.HasAccess || pkg.Frozen || pkg.IsUserEditable) continue;

                var release = FindLatestByChannel(pkg, "release");
                var dev = FindLatestByChannel(pkg, "dev");

                bool isInstalled = PurrPackageManagerInstaller.IsInstalled(pkg);
                if (!isInstalled) continue;

                bool isGitInstall = PurrPackageManagerInstaller.IsInstalledViaGit(pkg);
                var installedVersion = PurrPackageManagerInstaller.GetInstalledVersion(pkg);

                if (pkg.IsExternal && isGitInstall)
                {
                    if (TryGetExternalUpdateTarget(pkg, out var gitUrl, out var expectedCommit))
                        updates.Add((pkg, null, gitUrl, expectedCommit));
                }
                else
                {
                    var target = GetVersionUpdateTarget(pkg, installedVersion, release, dev);
                    if (target != null)
                        updates.Add((pkg, target, null, null));
                }
            }

            var catalogById = new Dictionary<string, PackageInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var package in _packages.Packages)
            {
                if (!string.IsNullOrEmpty(package.Id))
                    catalogById[package.Id] = package;
            }

            // Every missing dependency is promoted into the durable queue before its root. That way
            // a reload or failed root cannot leave a staged dependency outside final verification.
            var plannedById = new Dictionary<string, (
                PackageInfo pkg, VersionInfo version, string gitUrl, string expectedCommit)>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var update in updates)
            {
                if (!string.IsNullOrEmpty(update.pkg.Id))
                    plannedById[update.pkg.Id] = update;
            }

            var ordered = new List<(
                PackageInfo pkg, VersionInfo version, string gitUrl, string expectedCommit)>();
            var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var visitingUpdates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var visitingDependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddWithDependencies((
                PackageInfo pkg, VersionInfo version, string gitUrl, string expectedCommit) update)
            {
                var key = update.pkg.Id ?? update.pkg.GetUpmPackageName();
                if (string.IsNullOrEmpty(key) || added.Contains(key) || !visitingUpdates.Add(key))
                    return;

                string preferredChannel = update.version?.Channel;
                if (string.IsNullOrEmpty(preferredChannel))
                {
                    preferredChannel = string.Equals(update.gitUrl, update.pkg.GitInstallUrlDev,
                        StringComparison.Ordinal) ? "dev" : "release";
                }

                AddDependencies(update.pkg, preferredChannel);

                visitingUpdates.Remove(key);
                if (added.Add(key))
                    ordered.Add(update);
            }

            void AddDependencies(PackageInfo owner, string preferredChannel)
            {
                if (owner.DependencyIds == null)
                    return;

                foreach (var dependencyId in owner.DependencyIds)
                {
                    if (plannedById.TryGetValue(dependencyId, out var plannedDependency))
                    {
                        AddWithDependencies(plannedDependency);
                        continue;
                    }

                    if (!catalogById.TryGetValue(dependencyId, out var dependency))
                        continue;

                    if (!PurrPackageManagerInstaller.IsInstalled(dependency))
                    {
                        if (TryCreateDependencyInstall(dependency, preferredChannel,
                                out var dependencyInstall))
                        {
                            plannedById[dependencyId] = dependencyInstall;
                            AddWithDependencies(dependencyInstall);
                        }
                        continue;
                    }

                    // Installed dependencies with no update can still have a newly-added missing
                    // transitive dependency. Traverse them without adding an unnecessary update item.
                    if (visitingDependencies.Add(dependencyId))
                    {
                        AddDependencies(dependency, preferredChannel);
                        visitingDependencies.Remove(dependencyId);
                    }
                }
            }

            static bool TryCreateDependencyInstall(PackageInfo dependency, string preferredChannel,
                out (PackageInfo pkg, VersionInfo version, string gitUrl, string expectedCommit) install)
            {
                install = default;
                if (dependency == null || !dependency.HasAccess || dependency.IsUserEditable)
                    return false;

                if (dependency.IsExternal)
                {
                    bool preferDev = string.Equals(preferredChannel, "dev",
                        StringComparison.OrdinalIgnoreCase);
                    var gitUrl = preferDev
                        ? dependency.GitInstallUrlDev ?? dependency.GitInstallUrlRelease
                        : dependency.GitInstallUrlRelease ?? dependency.GitInstallUrlDev;
                    if (string.IsNullOrEmpty(gitUrl))
                        return false;

                    var expectedCommit = string.Equals(gitUrl, dependency.GitInstallUrlDev,
                        StringComparison.Ordinal)
                        ? dependency.LatestCommitDev
                        : dependency.LatestCommitRelease;
                    install = (dependency, null, gitUrl, expectedCommit);
                    return true;
                }

                var version = FindLatestByChannel(dependency, preferredChannel)
                              ?? (dependency.Versions != null && dependency.Versions.Length > 0
                                  ? dependency.Versions[0]
                                  : null);
                if (version == null)
                    return false;

                install = (dependency, version, null, null);
                return true;
            }

            foreach (var update in updates)
                AddWithDependencies(update);

            return ordered;
        }

        private void UpdateAllPackages()
        {
            if (_isUpdatingAll || _packages?.Packages == null)
                return;

            var updates = CollectUpdatablePackages();
            if (updates.Count == 0)
                return;

            var names = new StringBuilder();
            foreach (var (pkg, _, _, _) in updates)
                names.AppendLine($"\u2022 {pkg.DisplayName}");

            if (!EditorUtility.DisplayDialog("Update All Packages",
                $"The following {updates.Count} package(s) will be updated:\n\n{names}\n" +
                "User-editable packages are excluded and must be updated individually. " +
                "Required hidden or missing dependencies are included. Downloaded embedded packages " +
                "will still ask before replacing local changes.",
                "Update All", "Cancel"))
                return;

            var state = new PackageUpdateBatchState();
            foreach (var (pkg, version, gitUrl, expectedCommit) in updates)
            {
                state.Items.Add(new PackageUpdateBatchItem
                {
                    PackageId = pkg.Id,
                    UpmName = pkg.GetUpmPackageName(),
                    DisplayName = pkg.DisplayName,
                    VersionId = version?.Id,
                    Version = version?.Version,
                    GitUrl = gitUrl,
                    ExpectedCommit = expectedCommit,
                    ExpectedLockVersion = !string.IsNullOrEmpty(gitUrl)
                        ? gitUrl
                        : PurrPackageManagerInstaller.GetGitManifestValueForVersion(pkg, version),
                    DependencyIds = pkg.DependencyIds ?? Array.Empty<string>()
                });
            }

            SavePendingUpdateBatch(state);
            _isUpdatingAll = true;
            Repaint();
            _ = ResumePendingUpdateBatch();
        }

        private async System.Threading.Tasks.Task ResumePendingUpdateBatch()
        {
            if (_isRunningUpdateBatch || _packages?.Packages == null)
                return;
            if (!TryGetPendingUpdateBatch(out var state))
            {
                _isUpdatingAll = false;
                Repaint();
                return;
            }

            _isRunningUpdateBatch = true;
            _isUpdatingAll = true;
            Repaint();
            try
            {
                await PackageUpdateBatchRunner.Run(
                    state,
                    IsBatchItemApplied,
                    IsBatchItemResolved,
                    (item, resolve) => InstallBatchItem(state, item, resolve),
                    SavePendingUpdateBatch,
                    (index, count, item) =>
                    {
                        EditorUtility.DisplayProgressBar("Updating All Packages",
                            $"Updating {item.DisplayName} ({index + 1}/{count})...",
                            (float)(index + 1) / count);
                    },
                    EditorUtility.ClearProgressBar,
                    () =>
                    {
                        PurrPackageManagerCache.Invalidate();
                        Repaint();
                    },
                    () => PurrPackageManagerInstaller.ResolvePackagesWithRetry(
                        () => AreSuccessfulBatchItemsApplied(state)));

                // Keep the SessionState journal until Resolve returns or a new domain verifies its
                // targets. This preserves failures and keeps Update All disabled throughout UPM work.
                ClearPendingUpdateBatch();
                PurrPackageManagerCache.Invalidate();
            }
            catch (Exception e)
            {
                state.Errors ??= new List<string>();
                state.Errors.Add($"Update All: {e.Message}");
                ClearPendingUpdateBatch();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                _isRunningUpdateBatch = false;
                _isUpdatingAll = false;
                Repaint();
            }

            if (state.Errors != null && state.Errors.Count > 0)
                EditorUtility.DisplayDialog("Update Failed",
                    string.Join("\n", state.Errors), "Ok");

            LoadData();
        }

        private async System.Threading.Tasks.Task<Result<bool>> InstallBatchItem(
            PackageUpdateBatchState state, PackageUpdateBatchItem item, bool resolve)
        {
            var package = FindBatchPackage(item);
            if (package == null)
                return Result<bool>.Fail("The package is no longer present in the catalog.");

            var apiKey = PurrPackageManagerAuth.GetApiKey();
            Result<bool> result;
            if (!string.IsNullOrEmpty(item.GitUrl))
            {
                result = await PurrPackageManagerInstaller.InstallExternalWithDependencies(
                    apiKey, package, item.GitUrl, _packages.Packages, resolve,
                    allowUserEditableDependencies: false);
            }
            else
            {
                var version = FindBatchVersion(package, item);
                if (version == null)
                    return Result<bool>.Fail($"Version '{item.Version}' is no longer present in the catalog.");

                result = await PurrPackageManagerInstaller.InstallWithDependencies(
                    apiKey, package, version, _packages.Packages, resolve,
                    allowUserEditableDependencies: false,
                    onDownloadedPackagePrepared: actualVersion =>
                    {
                        if (string.IsNullOrEmpty(actualVersion))
                            return;

                        item.Version = actualVersion;
                        SavePendingUpdateBatch(state);
                    });
            }

            // Download metadata and package.json versions can differ. Store the actual embedded
            // version before the runner persists this item so final registration checks are exact.
            if (result.Success && string.IsNullOrEmpty(item.ExpectedLockVersion))
                item.Version = PurrPackageManagerInstaller.GetInstalledVersion(package);

            return result;
        }

        private bool IsBatchItemApplied(PackageUpdateBatchItem item)
        {
            var package = FindBatchPackage(item);
            if (package == null)
                return false;

            if (!string.IsNullOrEmpty(item.GitUrl))
            {
                var installedHash = PurrPackageManagerInstaller.GetInstalledCommitHash(package);
                if (!string.IsNullOrEmpty(item.ExpectedCommit))
                    return HashesMatch(installedHash, item.ExpectedCommit);
                return PurrPackageManagerInstaller.IsPackageLockVersion(
                    item.UpmName, item.ExpectedLockVersion);
            }

            return string.Equals(PurrPackageManagerInstaller.GetInstalledVersion(package), item.Version,
                StringComparison.OrdinalIgnoreCase);
        }

        private bool AreSuccessfulBatchItemsApplied(PackageUpdateBatchState state)
        {
            foreach (var item in state.Items)
            {
                if (item?.Succeeded != true)
                    continue;

                if (!IsBatchItemResolved(item))
                    return false;
            }

            // With no successful item there is no target to poll. Resolve is still issued once to
            // flush any conservative install attempt recorded by the runner.
            return true;
        }

        private bool IsBatchItemResolved(PackageUpdateBatchItem item)
        {
            if (!string.IsNullOrEmpty(item.ExpectedCommit))
            {
                var package = FindBatchPackage(item);
                var installedHash = package == null
                    ? null
                    : PurrPackageManagerInstaller.GetInstalledCommitHash(package);
                return HashesMatch(installedHash, item.ExpectedCommit);
            }

            if (!string.IsNullOrEmpty(item.ExpectedLockVersion))
            {
                return PurrPackageManagerInstaller.IsPackageLockVersion(
                    item.UpmName, item.ExpectedLockVersion);
            }

            return PurrPackageManagerInstaller.IsRegisteredPackageVersion(item.UpmName, item.Version);
        }

        private PackageInfo FindBatchPackage(PackageUpdateBatchItem item)
        {
            if (_packages?.Packages == null)
                return null;

            foreach (var package in _packages.Packages)
            {
                if ((!string.IsNullOrEmpty(item.PackageId)
                     && string.Equals(package.Id, item.PackageId, StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrEmpty(item.UpmName)
                        && string.Equals(package.GetUpmPackageName(), item.UpmName,
                            StringComparison.OrdinalIgnoreCase)))
                    return package;
            }

            return null;
        }

        private static VersionInfo FindBatchVersion(PackageInfo package, PackageUpdateBatchItem item)
        {
            if (package?.Versions == null)
                return null;

            foreach (var version in package.Versions)
            {
                if ((!string.IsNullOrEmpty(item.VersionId)
                     && string.Equals(version.Id, item.VersionId, StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrEmpty(item.Version)
                        && string.Equals(version.Version, item.Version, StringComparison.OrdinalIgnoreCase)))
                    return version;
            }

            return null;
        }

        private static bool HasPendingUpdateBatch()
        {
            return !string.IsNullOrEmpty(SessionState.GetString(PendingUpdateBatchSessionKey, string.Empty));
        }

        private static bool TryGetPendingUpdateBatch(out PackageUpdateBatchState state)
        {
            state = null;
            var json = SessionState.GetString(PendingUpdateBatchSessionKey, string.Empty);
            if (string.IsNullOrEmpty(json))
                return false;

            try
            {
                state = JsonUtility.FromJson<PackageUpdateBatchState>(json);
                if (state?.Items == null || state.Items.Count == 0)
                {
                    ClearPendingUpdateBatch();
                    state = null;
                    return false;
                }

                if (state.NextIndex < 0)
                    state.NextIndex = 0;
                if (state.NextIndex > state.Items.Count)
                    state.NextIndex = state.Items.Count;
                state.Errors ??= new List<string>();
                foreach (var item in state.Items)
                {
                    if (item != null)
                        item.DependencyIds ??= Array.Empty<string>();
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[PurrNet] Could not restore the pending package update batch: {e.Message}");
                ClearPendingUpdateBatch();
                return false;
            }
        }

        private static void SavePendingUpdateBatch(PackageUpdateBatchState state)
        {
            SessionState.SetString(PendingUpdateBatchSessionKey, JsonUtility.ToJson(state));
        }

        private static void ClearPendingUpdateBatch()
        {
            SessionState.EraseString(PendingUpdateBatchSessionKey);
        }

        private async void LoadData()
        {
            try
            {
                _isLoading = true;
                _errorMessage = null;
                Repaint();

                try
                {
                    var apiKey = PurrPackageManagerAuth.GetApiKey();
                    bool hasKey = !string.IsNullOrEmpty(apiKey);

                    if (hasKey)
                    {
                        if (PurrPackageManagerCache.TryGetEntitlements(out var cachedEntitlements))
                        {
                            _entitlements = cachedEntitlements;
                        }
                        else
                        {
                            var entitlementsResult = await PurrPackageManagerAPI.GetEntitlements(apiKey);
                            if (entitlementsResult.Success)
                            {
                                _entitlements = entitlementsResult.Value;
                                PurrPackageManagerCache.SetEntitlements(_entitlements);
                            }
                        }
                    }
                    else
                    {
                        _entitlements = null;
                    }

                    if (PurrPackageManagerCache.TryGetPackages(out var cachedPackages))
                    {
                        _packages = cachedPackages;
                    }
                    else
                    {
                        var packagesResult = await PurrPackageManagerAPI.GetPackages(apiKey);
                        if (packagesResult.Success)
                        {
                            _packages = packagesResult.Value;
                            PurrPackageManagerCache.SetPackages(_packages);
                        }
                        else
                        {
                            _errorMessage = packagesResult.Error;
                        }
                    }

                    _isLoading = false;
                    Repaint();

                    if (_packages?.Packages != null && HasPendingUpdateBatch()
                        && !_isRunningUpdateBatch)
                        _ = ResumePendingUpdateBatch();
                }
                catch (Exception e)
                {
                    _errorMessage = e.Message;
                    _isLoading = false;
                    Repaint();
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private void InstallExternalPackage(PackageInfo package, string gitUrl, string operationKey)
        {
            if (!TryBeginPackageOperation(operationKey))
                return;

            EditorApplication.delayCall += async () =>
            {
                try
                {
                    var apiKey = PurrPackageManagerAuth.GetApiKey();
                    var result = await PurrPackageManagerInstaller.InstallExternalWithDependencies(
                        apiKey, package, gitUrl, _packages?.Packages);
                    if (!result.Success)
                        EditorUtility.DisplayDialog("Install Failed", result.Error, "Ok");
                    LoadData();
                }
                catch (Exception e)
                {
                    EditorUtility.ClearProgressBar();
                    EditorUtility.DisplayDialog("Install Failed", e.Message, "Ok");
                    Repaint();
                }
                finally
                {
                    EndPackageOperation(operationKey);
                }
            };
        }

        private async void RemovePackage(PackageInfo package)
        {
            var operationKey = GetPackageOperationKey(package, "remove");
            if (!TryBeginPackageOperation(operationKey))
                return;

            try
            {
                var result = await PurrPackageManagerInstaller.Remove(package);
                if (!result.Success)
                    EditorUtility.DisplayDialog("Remove Failed", result.Error, "Ok");
                LoadData();
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Remove Failed", e.Message, "Ok");
            }
            finally
            {
                EndPackageOperation(operationKey);
            }
        }

        private async void InstallPackage(PackageInfo package, VersionInfo version, string operationKey = null)
        {
            if (string.IsNullOrEmpty(operationKey))
                operationKey = GetPackageOperationKey(package, version);

            if (!TryBeginPackageOperation(operationKey))
                return;

            try
            {
                var apiKey = PurrPackageManagerAuth.GetApiKey();
                var result = await PurrPackageManagerInstaller.InstallWithDependencies(
                    apiKey, package, version, _packages?.Packages);

                if (!result.Success)
                    EditorUtility.DisplayDialog("Install Failed", result.Error, "Ok");

                _releasePopupIndex = -1;
                _devPopupIndex = -1;
                _releasePopupTouched = false;
                _devPopupTouched = false;
                Repaint();
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Install Failed", e.Message, "Ok");
                Repaint();
            }
            finally
            {
                EndPackageOperation(operationKey);
            }
        }
    }
}
