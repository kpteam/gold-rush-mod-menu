using System;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using GoldDigger;
using HarmonyLib;
using Skins;
using UnityEngine;

namespace GoldRushModMenu
{
    [BepInPlugin("com.goldrushmod.modmenu", "Gold Rush Mod Menu", "1.6.0")]
    public class ModMenuPlugin : BaseUnityPlugin
    {
        private BepInEx.Configuration.ConfigEntry<int>   _cfgNuggetLimit;
        private BepInEx.Configuration.ConfigEntry<float> _cfgGoldPrice;
        private BepInEx.Configuration.ConfigEntry<bool>  _cfgPriceOverrideOn;
        private BepInEx.Configuration.ConfigEntry<int>   _cfgDeclineSpeed;
        private BepInEx.Configuration.ConfigEntry<float> _cfgCashTrickleRate;
        private BepInEx.Configuration.ConfigEntry<float> _cfgGoldTrickleRate;

        private bool  _tricklingCash;
        private float _trickCashTarget;
        private bool  _tricklingGold;
        private float _trickGoldTarget;

        private bool    _showMenu;
        private Rect    _windowRect = new Rect(20f, 20f, 440f, 50f);
        private Vector2 _scrollPos;

        private bool _secCash     = true;
        private bool _secGold     = true;
        private bool _secDiamonds = false;
        private bool _secPrice    = false;
        private bool _secNuggets  = false;

        private string _cashInput;
        private string _goldInput;
        private string _diamondsInput;
        private string _goldPriceInput;
        private string _nuggetLimitInput;
        private string _cashRateInput;
        private string _goldRateInput;

        private bool  _priceOverrideOn;
        private float _noiseTimer;
        private float _noiseBump;
        private CursorLockMode _savedLockMode;
        private bool           _savedCursorVisible;
        private string _statusMsg  = string.Empty;
        private float  _statusTimer;

        private GUIStyle _headerStyle;
        private GUIStyle _statusStyle;
        private GUIStyle _stealthStyle;
        private GUIStyle _pausedStyle;
        private GUIStyle _decliningStyle;
        private GUIStyle _sectionStyle;
        private GUIStyle _trickleStyle;
        private bool     _stylesBuilt;

        private static PropertyInfo _lbProp;
        private static object       _lbInstance;
        private static float        _lbCheckTimer;

        private void Awake()
        {
            _cfgNuggetLimit     = Config.Bind("Nuggets",   "NuggetLimit",    60,     "Max random gold nuggets (game default = 60).");
            _cfgGoldPrice       = Config.Bind("GoldPrice", "LockedPrice",    1200f,  "Last locked gold price in dollars per oz.");
            _cfgPriceOverrideOn = Config.Bind("GoldPrice", "OverrideActive", false,  "Whether price override was active on last exit.");
            _cfgDeclineSpeed    = Config.Bind("GoldPrice", "DeclineSpeed",   1,      "0=Slow 1=Normal 2=Fast 3=Instant.");
            _cfgCashTrickleRate = Config.Bind("Trickle",   "CashRatePerSec", 5000f,  "Cash added per second during trickle.");
            _cfgGoldTrickleRate = Config.Bind("Trickle",   "GoldRatePerSec", 50f,    "Gold added per second during trickle in oz.");

            _nuggetLimitInput = _cfgNuggetLimit.Value.ToString();
            _goldPriceInput   = _cfgGoldPrice.Value.ToString("0");
            _cashRateInput    = _cfgCashTrickleRate.Value.ToString("0");
            _goldRateInput    = _cfgGoldTrickleRate.Value.ToString("0");
            _cashInput        = "100000";
            _goldInput        = "500";
            _diamondsInput    = "500";

            NuggetExtensionPatch.NuggetLimit = _cfgNuggetLimit.Value;

            if (_cfgPriceOverrideOn.Value)
            {
                GoldPricePatch.OverrideValue   = _cfgGoldPrice.Value;
                GoldPricePatch.OverrideEnabled = true;
                _priceOverrideOn = true;
            }

            new Harmony("com.goldrushmod.modmenu").PatchAll();
            Logger.LogInfo("Gold Rush Mod Menu v1.6 loaded.");
        }

        private void Update()
        {
            _lbCheckTimer -= Time.unscaledDeltaTime;
            if (_lbCheckTimer <= 0f)
            {
                _lbCheckTimer = 2f;
                bool lb = IsLeaderboard();
                GoldPricePatch.Disabled      = lb;
                NuggetExtensionPatch.Disabled = lb;
                if (lb && _showMenu)
                {
                    _showMenu = false;
                    Cursor.lockState = _savedLockMode;
                    Cursor.visible   = _savedCursorVisible;
                }
            }

            if (Input.GetKeyDown(KeyCode.F10) && !IsLeaderboard())
            {
                _showMenu = !_showMenu;
                if (_showMenu) { _savedLockMode = Cursor.lockState; _savedCursorVisible = Cursor.visible; }
                else           { Cursor.lockState = _savedLockMode; Cursor.visible = _savedCursorVisible; }
            }

            if (!IsLeaderboard())
            {
                if (Input.GetKeyDown(KeyCode.Keypad4)) StartCashTrickle(50000m);
                if (Input.GetKeyDown(KeyCode.Keypad5)) StartCashTrickle(250000m);
                if (Input.GetKeyDown(KeyCode.Keypad6)) StartGoldTrickle(500m);
                if (Input.GetKeyDown(KeyCode.Keypad7)) StartGoldTrickle(2500m);
            }

            if (_statusTimer > 0f) _statusTimer -= Time.unscaledDeltaTime;
            TickGoldPriceTransition();
            TickTrickle();
        }

        private void LateUpdate()
        {
            if (!_showMenu) return;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
        }

        private void OnGUI()
        {
            if (!_showMenu) return;
            BuildStyles();
            GUI.backgroundColor = new Color(0.10f, 0.10f, 0.12f, 0.97f);
            _windowRect.width  = 440f;
            _windowRect.height = Mathf.Min(Screen.height * 0.85f, 660f);
            _windowRect = GUILayout.Window(90_010, _windowRect, DrawWindow, string.Empty);
        }

        private void DrawWindow(int id)
        {
            GUILayout.Label("GOLD RUSH MOD MENU  v1.6", _headerStyle);
            bool lb = IsLeaderboard();
            if (lb)
                GUILayout.Label("LEADERBOARD SESSION - all patches disabled", _stealthStyle);
            else
                GUILayout.Label("F10=toggle  Num4=+$50k  Num5=+$250k  Num6=+500oz  Num7=+2500oz", _statusStyle);

            GUILayout.Space(4f); DrawSeparator();
            _scrollPos = GUILayout.BeginScrollView(_scrollPos, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);

            if (SectionToggle(ref _secCash, "CASH (Money)"))
            {
                DrawInfo("Current: $" + GetCurrentCash().ToString("N0"));
                _cashInput     = DrawInputRow("Amount:", _cashInput);
                DrawInfo("Trickle rate: " + _cfgCashTrickleRate.Value.ToString("N0") + " $/s");
                _cashRateInput = DrawInputRow("Rate $/s:", _cashRateInput);
                if (SmallButton("Save Rate")) SaveCashRate();
                GUILayout.BeginHorizontal();
                if (!lb) { if (SmallButton("Trickle")) TrickleSetCash(); if (SmallButton("Set Now")) SetCash(); }
                if (SmallButton("+$50k"))  StartCashTrickle(50000m);
                if (SmallButton("+$250k")) StartCashTrickle(250000m);
                if (SmallButton("+$1M"))   StartCashTrickle(1000000m);
                GUILayout.EndHorizontal();
                if (_tricklingCash)
                {
                    GUILayout.Label("  trickling to $" + _trickCashTarget.ToString("N0") + "...", _trickleStyle);
                    if (SmallButton("Stop")) _tricklingCash = false;
                }
                GUILayout.Space(4f);
            }
            DrawSeparator();

            if (SectionToggle(ref _secGold, "GOLD INVENTORY (oz)"))
            {
                DrawInfo("Current: " + GetCurrentGold().ToString("N2") + " oz");
                _goldInput     = DrawInputRow("Amount:", _goldInput);
                DrawInfo("Trickle rate: " + _cfgGoldTrickleRate.Value.ToString("N2") + " oz/s");
                _goldRateInput = DrawInputRow("Rate oz/s:", _goldRateInput);
                if (SmallButton("Save Rate")) SaveGoldRate();
                GUILayout.BeginHorizontal();
                if (!lb) { if (SmallButton("Trickle")) TrickleSetGold(); if (SmallButton("Set Now")) SetGold(); }
                if (SmallButton("+500 oz"))   StartGoldTrickle(500m);
                if (SmallButton("+2,500 oz")) StartGoldTrickle(2500m);
                GUILayout.EndHorizontal();
                if (_tricklingGold)
                {
                    GUILayout.Label("  trickling to " + _trickGoldTarget.ToString("N2") + " oz...", _trickleStyle);
                    if (SmallButton("Stop")) _tricklingGold = false;
                }
                GUILayout.Space(4f);
            }
            DrawSeparator();

            if (SectionToggle(ref _secDiamonds, "DIAMONDS (Gems)"))
            {
                DrawInfo("Current: " + GetCurrentDiamonds().ToString("N0"));
                _diamondsInput = DrawInputRow("Amount:", _diamondsInput);
                GUILayout.BeginHorizontal();
                if (!lb) { if (SmallButton("Set")) SetDiamonds(); }
                if (SmallButton("+100"))   AddDiamonds(100m);
                if (SmallButton("+500"))   AddDiamonds(500m);
                if (SmallButton("+1,000")) AddDiamonds(1000m);
                GUILayout.EndHorizontal();
                GUILayout.Space(4f);
            }
            DrawSeparator();

            if (SectionToggle(ref _secPrice, "GOLD STOCK PRICE ($/oz)"))
            {
                if (lb) DrawInfo("Disabled during leaderboard session.");
                else    DrawGoldPriceSection();
                GUILayout.Space(4f);
            }
            DrawSeparator();

            if (SectionToggle(ref _secNuggets, "RANDOM GOLD NUGGETS"))
            {
                if (lb) DrawInfo("Disabled during leaderboard session.");
                else    DrawNuggetSection();
                GUILayout.Space(4f);
            }

            GUILayout.EndScrollView();
            DrawSeparator();
            GUILayout.Label(_statusTimer > 0f ? _statusMsg : " ", _statusStyle);
            GUI.DragWindow(new Rect(0f, 0f, _windowRect.width, 40f));
        }

        private void TickTrickle()
        {
            var w = Wallet; if (w == null) return;
            if (_tricklingCash)
            {
                decimal cur = (decimal)w.Cash.Value, tgt = (decimal)_trickCashTarget;
                decimal add = Math.Min((decimal)(_cfgCashTrickleRate.Value * Time.unscaledDeltaTime), tgt - cur);
                if (cur >= tgt) { _tricklingCash = false; ShowStatus("Cash trickle complete."); }
                else w.Cash = new FixedCash(cur + add);
            }
            if (_tricklingGold)
            {
                decimal cur = (decimal)w.Gold.Value, tgt = (decimal)_trickGoldTarget;
                decimal add = Math.Min((decimal)(_cfgGoldTrickleRate.Value * Time.unscaledDeltaTime), tgt - cur);
                if (cur >= tgt) { _tricklingGold = false; ShowStatus("Gold trickle complete."); }
                else w.Gold = new FixedGold(cur + add);
            }
        }

        private void StartCashTrickle(decimal amount)
        {
            var w = Wallet; if (w == null) return;
            _trickCashTarget = (float)((decimal)w.Cash.Value + amount); _tricklingCash = true;
            ShowStatus("Trickling cash to $" + _trickCashTarget.ToString("N0"));
        }
        private void StartGoldTrickle(decimal amount)
        {
            var w = Wallet; if (w == null) return;
            _trickGoldTarget = (float)((decimal)w.Gold.Value + amount); _tricklingGold = true;
            ShowStatus("Trickling gold to " + _trickGoldTarget.ToString("N2") + " oz");
        }
        private void TrickleSetCash()
        {
            if (!TryParseNonNeg(_cashInput, out decimal v)) return;
            var w = Wallet; if (w == null) { ShowStatus("Load a save first."); return; }
            _trickCashTarget = (float)v; _tricklingCash = true;
            ShowStatus("Trickling cash to $" + v.ToString("N0"));
        }
        private void TrickleSetGold()
        {
            if (!TryParseNonNeg(_goldInput, out decimal v)) return;
            var w = Wallet; if (w == null) { ShowStatus("Load a save first."); return; }
            _trickGoldTarget = (float)v; _tricklingGold = true;
            ShowStatus("Trickling gold to " + v.ToString("N2") + " oz");
        }
        private void SaveCashRate()
        {
            if (!float.TryParse(_cashRateInput, out float r) || r <= 0f) { ShowStatus("Enter a positive number for cash rate."); return; }
            _cfgCashTrickleRate.Value = r; ShowStatus("Cash trickle rate: $" + r.ToString("N0") + "/s (saved)");
        }
        private void SaveGoldRate()
        {
            if (!float.TryParse(_goldRateInput, out float r) || r <= 0f) { ShowStatus("Enter a positive number for gold rate."); return; }
            _cfgGoldTrickleRate.Value = r; ShowStatus("Gold trickle rate: " + r.ToString("N2") + " oz/s (saved)");
        }

        private void DrawGoldPriceSection()
        {
            bool inDecline = GoldPricePatch.InTransition;
            if (_priceOverrideOn)
            {
                GoldPricePatch.BypassForReal = true;
                GUILayout.Label("PAUSED  locked $" + GoldPricePatch.OverrideValue.ToString("N2") + "/oz  market $" + GetRawMarketPrice().ToString("N2") + "/oz", _pausedStyle);
            }
            else if (inDecline)
                GUILayout.Label("DECLINING  $" + GoldPricePatch.TransitionPrice.ToString("N2") + " -> $" + GoldPricePatch.TransitionTarget.ToString("N2") + "/oz", _decliningStyle);
            else
                DrawInfo("LIVE  $" + GetActiveGoldPrice().ToString("N2") + "/oz");

            _goldPriceInput = DrawInputRow("$/oz:", _goldPriceInput);
            GUILayout.BeginHorizontal();
            if (SmallButton("Apply Price")) ApplyPriceOverride();
            if (_priceOverrideOn) { GUI.color = new Color(1f,0.55f,0.15f); if (SmallButton("Start Decline")) StartDecline(); }
            else if (inDecline)   { GUI.color = new Color(0.4f,0.8f,1f);  if (SmallButton("Pause Decline")) PauseDecline(); }
            else                  { GUI.color = new Color(0.5f,1f,0.5f);  if (SmallButton("Pause Market"))  ApplyPriceOverride(); }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (SmallButton("$500\nCrash"))     ApplyPreset(500f);
            if (SmallButton("$1,200\nDefault")) ApplyPreset(1200f);
            if (SmallButton("$2,500\nHigh"))    ApplyPreset(2500f);
            if (SmallButton("$5,000\nBoom"))    ApplyPreset(5000f);
            if (SmallButton("$9,999\nMax"))     ApplyPreset(9999f);
            GUILayout.EndHorizontal();

            int sp = _cfgDeclineSpeed.Value;
            DrawInfo("Decline speed:");
            GUILayout.BeginHorizontal();
            GUI.color = sp==0?new Color(0.5f,1f,0.5f):Color.white; if(SmallButton("Slow"))    SetDeclineSpeed(0);
            GUI.color = sp==1?new Color(0.5f,1f,0.5f):Color.white; if(SmallButton("Normal"))  SetDeclineSpeed(1);
            GUI.color = sp==2?new Color(0.5f,1f,0.5f):Color.white; if(SmallButton("Fast"))    SetDeclineSpeed(2);
            GUI.color = sp==3?new Color(1f,0.5f,0.5f):Color.white; if(SmallButton("Instant")) SetDeclineSpeed(3);
            GUI.color = Color.white;
            GUILayout.EndHorizontal();
        }

        private void ApplyPriceOverride()
        {
            if (!ParsePrice(_goldPriceInput, out float v)) return;
            GoldPricePatch.OverrideValue=v; GoldPricePatch.OverrideEnabled=true; GoldPricePatch.InTransition=false;
            _priceOverrideOn=true; _cfgGoldPrice.Value=v; _cfgPriceOverrideOn.Value=true;
            ShowStatus("Gold price locked at $"+v.ToString("N2")+"/oz");
        }
        private void ApplyPreset(float price)
        {
            _goldPriceInput = price.ToString("0");
            GoldPricePatch.OverrideValue=price; GoldPricePatch.OverrideEnabled=true; GoldPricePatch.InTransition=false;
            _priceOverrideOn=true; _cfgGoldPrice.Value=price; _cfgPriceOverrideOn.Value=true;
            ShowStatus("Gold price preset $"+price.ToString("N0")+"/oz");
        }
        private void SetDeclineSpeed(int s) { _cfgDeclineSpeed.Value=s; string[]n={"Slow","Normal","Fast","Instant"}; ShowStatus("Decline: "+n[Mathf.Clamp(s,0,3)]); }
        private void StartDecline()
        {
            float sp=GoldPricePatch.OverrideValue; GoldPricePatch.BypassForReal=true; float tgt=GetRawMarketPrice();
            GoldPricePatch.TransitionPrice=sp; GoldPricePatch.TransitionTarget=tgt;
            GoldPricePatch.OverrideEnabled=false; GoldPricePatch.InTransition=true;
            _priceOverrideOn=false; _noiseTimer=0f; _noiseBump=0f; _cfgPriceOverrideOn.Value=false;
            ShowStatus("Declining $"+sp.ToString("N0")+" -> $"+tgt.ToString("N0")+"/oz");
        }
        private void PauseDecline()
        {
            float l=GoldPricePatch.TransitionPrice; _goldPriceInput=l.ToString("0");
            GoldPricePatch.OverrideValue=l; GoldPricePatch.OverrideEnabled=true; GoldPricePatch.InTransition=false;
            _priceOverrideOn=true; _cfgGoldPrice.Value=l; _cfgPriceOverrideOn.Value=true;
            ShowStatus("Decline paused at $"+l.ToString("N0")+"/oz");
        }
        private void TickGoldPriceTransition()
        {
            if (!GoldPricePatch.InTransition) return;
            GoldPricePatch.BypassForReal=true;
            GoldPricePatch.TransitionTarget=GetRawMarketPrice();
            int sp = _cfgDeclineSpeed!=null ? _cfgDeclineSpeed.Value : 1;
            if (sp==3) { GoldPricePatch.TransitionPrice=GoldPricePatch.TransitionTarget; GoldPricePatch.InTransition=false; _cfgPriceOverrideOn.Value=false; ShowStatus("Price returned to market."); return; }
            float[] m={0.25f,1f,4f};
            _noiseTimer-=Time.deltaTime;
            if (_noiseTimer<=0f) { _noiseBump=UnityEngine.Random.Range(-60f,60f); _noiseTimer=UnityEngine.Random.Range(2f,4f); }
            float cur=GoldPricePatch.TransitionPrice, tgt=GoldPricePatch.TransitionTarget;
            float step=(50f+Mathf.Abs(cur-tgt)*0.08f)*Time.deltaTime*m[Mathf.Clamp(sp,0,2)];
            if (Mathf.Abs(cur-tgt)<30f) { GoldPricePatch.TransitionPrice=tgt; GoldPricePatch.InTransition=false; _cfgPriceOverrideOn.Value=false; ShowStatus("Price returned to market."); return; }
            GoldPricePatch.TransitionPrice=Mathf.MoveTowards(cur,tgt+_noiseBump,step);
        }

        private void DrawNuggetSection()
        {
            int cap=NuggetExtensionPatch.NuggetLimit, idx=GetCurrentNuggetIndex();
            DrawInfo("Cap: "+cap+"   spawned: "+idx+" / "+cap);
            _nuggetLimitInput = DrawInputRow("New cap:", _nuggetLimitInput);
            GUILayout.BeginHorizontal();
            if (SmallButton("Set Limit"))     ApplyNuggetLimit();
            if (SmallButton("Force Nugget"))  ForceNugget();
            if (SmallButton("Reset Counter")) ResetNuggetCounter();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (SmallButton("x2 (120)"))  { _nuggetLimitInput="120";  ApplyNuggetLimit(); }
            if (SmallButton("x5 (300)"))  { _nuggetLimitInput="300";  ApplyNuggetLimit(); }
            if (SmallButton("x10 (600)")) { _nuggetLimitInput="600";  ApplyNuggetLimit(); }
            if (SmallButton("Unlimited")) { _nuggetLimitInput="9999"; ApplyNuggetLimit(); }
            GUILayout.EndHorizontal();
        }
        private void ApplyNuggetLimit()
        {
            if (!int.TryParse(_nuggetLimitInput.Replace(",","").Replace(" ",""), out int v)||v<1) { ShowStatus("Enter a whole number >= 1."); return; }
            NuggetExtensionPatch.NuggetLimit=v; _cfgNuggetLimit.Value=v; ShowStatus("Nugget cap -> "+v+" (saved)");
        }
        private void ForceNugget()
        {
            var stats=Singleton<GameStatistics>.Instance; var wallet=Singleton<PlayerWallet>.Instance;
            if (stats==null||wallet==null) { ShowStatus("Load a save first."); return; }
            int cap=NuggetExtensionPatch.NuggetLimit;
            if (stats.CurrentNugget>=cap) { ShowStatus("Nugget cap already reached."); return; }
            const int aLen=60,cStart=55;
            int idx=stats.CurrentNugget<aLen?stats.CurrentNugget:cStart+((stats.CurrentNugget-aLen)%(aLen-cStart));
            float oz=stats.GoldNuggetOz[idx]*UnityEngine.Random.Range(0.6f,1.4f);
            wallet.Gold=new FixedGold(wallet.Gold.Value+(decimal)oz);
            var hud=Singleton<HUD>.Instance;
            if (hud!=null) hud.ShowHint("Bonus nugget: +"+oz.ToString("0.00")+" oz");
            stats.CurrentMudCount=0f; stats.CurrentNugget++; stats.LastNuggetTime=UnityEngine.Time.realtimeSinceStartup;
            ShowStatus("Nugget #"+stats.CurrentNugget+" forced: +"+oz.ToString("0.00")+" oz",6f);
        }
        private static void ResetNuggetCounter() { var s=Singleton<GameStatistics>.Instance; if(s==null)return; s.CurrentNugget=0; s.CurrentMudCount=0f; }
        private static int GetCurrentNuggetIndex() { var s=Singleton<GameStatistics>.Instance; return s!=null?s.CurrentNugget:0; }

        private static PlayerWallet Wallet => Singleton<PlayerWallet>.Instance;
        private static decimal GetCurrentCash()     { var w=Wallet; return w!=null?(decimal)w.Cash.Value:0m; }
        private static decimal GetCurrentGold()     { var w=Wallet; return w!=null?(decimal)w.Gold.Value:0m; }
        private static decimal GetCurrentDiamonds() { var w=Wallet; return w!=null?(decimal)w.Diamonds.Value:0m; }
        private bool WalletReady(out PlayerWallet w) { w=Wallet; if(w!=null)return true; ShowStatus("Load a save first."); return false; }
        private void SetCash()     { if(!TryParseNonNeg(_cashInput,  out decimal v)||!WalletReady(out var w))return; w.Cash    =new FixedCash(v); ShowStatus("Cash set $"+v.ToString("N0")); }
        private void SetGold()     { if(!TryParseNonNeg(_goldInput,  out decimal v)||!WalletReady(out var w))return; w.Gold    =new FixedGold(v); ShowStatus("Gold set "+v.ToString("N2")+" oz"); }
        private void SetDiamonds() { if(!TryParseNonNeg(_diamondsInput,out decimal v)||!WalletReady(out var w))return; w.Diamonds=new FixedGold(v); ShowStatus("Diamonds set "+v.ToString("N0")); }
        private void AddDiamonds(decimal a) { if(!WalletReady(out var w))return; w.Diamonds=new FixedGold(w.Diamonds.Value+a); ShowStatus("+"+a.ToString("N0")+" diamonds"); }
        private static float GetActiveGoldPrice() { var f=Singleton<Finance>.Instance; return f!=null?f.GetGoldPrice():0f; }
        private static float GetRawMarketPrice()  { var f=Singleton<Finance>.Instance; return f!=null?f.GetGoldPrice():1200f; }
        private bool ParsePrice(string t,out float v) { if(!float.TryParse(t.Replace(" ","").Replace(",",""),out v)||v<=0f){ShowStatus("Enter a positive price.");return false;} return true; }
        private static bool TryParseNonNeg(string t,out decimal v) { if(decimal.TryParse(t.Replace(" ","").Replace(",",""),out v)&&v>=0m)return true; v=0m; return false; }

        private static bool IsLeaderboard()
        {
            try
            {
                if (_lbProp == null)
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        var t = asm.GetType("SaveSystem.CheckPointManagerBase");
                        if (t == null) continue;
                        _lbProp = t.GetProperty("isLeadeboardMode", BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                        if (_lbProp != null) break;
                    }
                    if (_lbProp == null) return false;
                }
                if (_lbInstance == null)
                {
                    var ip = _lbProp.DeclaringType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                    _lbInstance = ip?.GetValue(null);
                }
                return _lbInstance != null && (bool)_lbProp.GetValue(_lbInstance);
            }
            catch { return false; }
        }

        private void ShowStatus(string msg, float duration = 4f) { _statusMsg=msg; _statusTimer=duration; Logger.LogInfo(msg); }

        private void BuildStyles()
        {
            if (_stylesBuilt) return;
            _headerStyle    = new GUIStyle(GUI.skin.label) { fontSize=14, fontStyle=FontStyle.Bold, alignment=TextAnchor.MiddleCenter };
            _headerStyle.normal.textColor    = new Color(1f,0.82f,0.2f);
            _statusStyle    = new GUIStyle(GUI.skin.label) { fontSize=10, alignment=TextAnchor.MiddleCenter };
            _statusStyle.normal.textColor    = new Color(0.75f,0.95f,0.75f);
            _stealthStyle   = new GUIStyle(GUI.skin.label) { fontSize=11, alignment=TextAnchor.MiddleCenter, fontStyle=FontStyle.Bold };
            _stealthStyle.normal.textColor   = new Color(1f,0.3f,0.3f);
            _pausedStyle    = new GUIStyle(GUI.skin.label) { fontSize=11 };
            _pausedStyle.normal.textColor    = new Color(1f,0.65f,0.2f);
            _decliningStyle = new GUIStyle(GUI.skin.label) { fontSize=11 };
            _decliningStyle.normal.textColor = new Color(0.4f,0.85f,1f);
            _trickleStyle   = new GUIStyle(GUI.skin.label) { fontSize=10, fontStyle=FontStyle.Italic };
            _trickleStyle.normal.textColor   = new Color(0.6f,1f,0.6f);
            _sectionStyle   = new GUIStyle(GUI.skin.button) { fontStyle=FontStyle.Bold, alignment=TextAnchor.MiddleLeft };
            _sectionStyle.normal.textColor   = new Color(0.95f,0.95f,1f);
            _sectionStyle.hover.textColor    = Color.white;
            _stylesBuilt = true;
        }

        private bool SectionToggle(ref bool expanded, string title)
        {
            if (GUILayout.Button((expanded?"▼ ":"▶ ")+title, _sectionStyle)) expanded=!expanded;
            return expanded;
        }
        private static void DrawInfo(string t) { var s=new GUIStyle(GUI.skin.label){fontSize=11}; s.normal.textColor=new Color(0.7f,0.85f,1f); GUILayout.Label(t,s); }
        private static string DrawInputRow(string label, string current) { GUILayout.BeginHorizontal(); GUILayout.Label(label,GUILayout.Width(70f)); string r=GUILayout.TextField(current,GUILayout.ExpandWidth(true)); GUILayout.EndHorizontal(); return r; }
        private static bool SmallButton(string t) => GUILayout.Button(t, GUILayout.ExpandWidth(true));
        private static void DrawSeparator() { Rect r=GUILayoutUtility.GetRect(1f,1f,GUILayout.ExpandWidth(true)); GUI.color=new Color(0.5f,0.5f,0.5f,0.5f); GUI.DrawTexture(r,Texture2D.whiteTexture); GUI.color=Color.white; GUILayout.Space(3f); }
    }

    [HarmonyPatch(typeof(Finance), nameof(Finance.GetGoldPrice), typeof(float))]
    internal static class GoldPricePatch
    {
        internal static bool  Disabled        { get; set; }
        internal static bool  OverrideEnabled { get; set; }
        internal static float OverrideValue   { get; set; } = 1200f;
        internal static bool  InTransition    { get; set; }
        internal static float TransitionPrice { get; set; }
        internal static float TransitionTarget{ get; set; }
        internal static bool  BypassForReal   { get; set; }
        static bool Prefix(ref float __result)
        {
            if (Disabled)        return true;
            if (BypassForReal)   { BypassForReal=false; return true; }
            if (OverrideEnabled) { __result=OverrideValue;   return false; }
            if (InTransition)    { __result=TransitionPrice; return false; }
            return true;
        }
    }

    [HarmonyPatch(typeof(GameStatistics), "AddMud")]
    internal static class NuggetExtensionPatch
    {
        internal static int  NuggetLimit { get; set; } = 60;
        internal static bool Disabled    { get; set; }
        private const int CycleStart=55, ArrayLen=60;
        static bool Prefix(GameStatistics __instance, float mud)
        {
            if (Disabled) return true;
            if (__instance.CurrentNugget<ArrayLen||NuggetLimit<=ArrayLen) return true;
            if (__instance.CurrentNugget>=NuggetLimit) return true;
            __instance.CurrentMudCount+=mud;
            int ci=CycleStart+((__instance.CurrentNugget-ArrayLen)%(ArrayLen-CycleStart));
            if (__instance.CurrentMudCount>__instance.DirtM3[ci] && UnityEngine.Time.realtimeSinceStartup-__instance.LastNuggetTime>10f)
            {
                float oz=__instance.GoldNuggetOz[ci]*UnityEngine.Random.Range(0.6f,1.4f);
                var w=Singleton<PlayerWallet>.Instance; if(w!=null) w.Gold=new FixedGold(w.Gold.Value+(decimal)oz);
                var h=Singleton<HUD>.Instance; if(h!=null) h.ShowHint("Bonus nugget #"+(__instance.CurrentNugget-ArrayLen+1)+": +"+oz.ToString("0.00")+" oz");
                __instance.CurrentMudCount=0f; __instance.CurrentNugget++; __instance.LastNuggetTime=UnityEngine.Time.realtimeSinceStartup;
            }
            return false;
        }
    }
}
