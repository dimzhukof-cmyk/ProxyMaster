using System.Collections.Generic;
using System.ComponentModel;

namespace ProxyMaster.Core;

/// <summary>
/// Сервис локализации. Предоставляет строки по ключу для текущего языка.
/// Binding-friendly: {Binding Loc[key]} обновляется при смене языка.
/// </summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
    public static readonly LocalizationService Instance = new();

    private string _language = "ru";

    public string Language
    {
        get => _language;
        set
        {
            _language = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        }
    }

    public string this[string key]
    {
        get
        {
            if (_strings.TryGetValue(_language, out var dict) && dict.TryGetValue(key, out var val))
                return val;
            if (_strings.TryGetValue("en", out var en) && en.TryGetValue(key, out var enVal))
                return enVal;
            return key;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public static readonly (string Code, string Name)[] Languages =
    {
        ("en", "English"),
        ("ru", "Русский"),
        ("de", "Deutsch"),
        ("es", "Español"),
        ("fr", "Français"),
        ("it", "Italiano"),
        ("zh", "中文"),
    };

    private readonly Dictionary<string, Dictionary<string, string>> _strings = new()
    {
        ["en"] = new()
        {
            ["app_subtitle"]   = "system proxy for Windows",
            ["status_active"]  = "ACTIVE",
            ["status_stopped"] = "STOPPED",
            ["btn_start"]      = "▶  START",
            ["btn_stop"]       = "■  STOP",
            ["lbl_type"]       = "Type",
            ["lbl_host"]       = "Server address",
            ["lbl_port"]       = "Port",
            ["lbl_login"]      = "Login (optional)",
            ["lbl_password"]   = "Password",
            ["btn_save"]       = "Save",
            ["lbl_traffic"]    = "Traffic:",
            ["opt_all_apps"]   = "All apps",
            ["opt_selected"]   = "Selected only",
            ["btn_refresh"]    = "↻  Refresh list",
            ["sel_count"]      = "Selected: {0} of {1}",
            ["lbl_log"]        = "Event log",
            ["btn_clear"]      = "Clear",
            ["lbl_transferred"]= "Transferred: ",
            ["lbl_table"]      = "Connections: ",
            ["lbl_process"]    = "Process",
            ["lbl_path"]       = "Path",
            ["btn_settings"]   = "⚙",
            ["settings_title"] = "Settings",
            ["settings_lang"]  = "Language",
            ["settings_theme"] = "Color scheme",
            ["btn_close"]      = "Close",
            ["theme_dark"]     = "Dark",
            ["theme_light"]    = "Light",
            ["theme_blue"]     = "Dark Blue",
            ["theme_green"]    = "Dark Green",
            ["theme_purple"]   = "Dark Purple",
            ["lbl_saved_servers"] = "Saved servers",
            ["lbl_search"]     = "Search...",
        },
        ["ru"] = new()
        {
            ["app_subtitle"]   = "системный прокси для Windows",
            ["status_active"]  = "АКТИВЕН",
            ["status_stopped"] = "ОСТАНОВЛЕН",
            ["btn_start"]      = "▶  ЗАПУСТИТЬ",
            ["btn_stop"]       = "■  СТОП",
            ["lbl_type"]       = "Тип",
            ["lbl_host"]       = "Адрес сервера",
            ["lbl_port"]       = "Порт",
            ["lbl_login"]      = "Логин (необязательно)",
            ["lbl_password"]   = "Пароль",
            ["btn_save"]       = "Сохранить",
            ["lbl_traffic"]    = "Трафик:",
            ["opt_all_apps"]   = "Все приложения",
            ["opt_selected"]   = "Только выбранные",
            ["btn_refresh"]    = "↻  Обновить список",
            ["sel_count"]      = "Выбрано: {0} из {1}",
            ["lbl_log"]        = "Журнал событий",
            ["btn_clear"]      = "Очистить",
            ["lbl_transferred"]= "Передано: ",
            ["lbl_table"]      = "Соединений: ",
            ["lbl_process"]    = "Процесс",
            ["lbl_path"]       = "Путь",
            ["btn_settings"]   = "⚙",
            ["settings_title"] = "Настройки",
            ["settings_lang"]  = "Язык",
            ["settings_theme"] = "Цветовая схема",
            ["btn_close"]      = "Закрыть",
            ["theme_dark"]     = "Тёмная",
            ["theme_light"]    = "Светлая",
            ["theme_blue"]     = "Тёмно-синяя",
            ["theme_green"]    = "Тёмно-зелёная",
            ["theme_purple"]   = "Тёмно-фиолетовая",
            ["lbl_saved_servers"] = "Серверы",
            ["lbl_search"]     = "Поиск...",
        },
        ["de"] = new()
        {
            ["app_subtitle"]   = "System-Proxy für Windows",
            ["status_active"]  = "AKTIV",
            ["status_stopped"] = "GESTOPPT",
            ["btn_start"]      = "▶  STARTEN",
            ["btn_stop"]       = "■  STOPP",
            ["lbl_type"]       = "Typ",
            ["lbl_host"]       = "Serveradresse",
            ["lbl_port"]       = "Port",
            ["lbl_login"]      = "Benutzer (optional)",
            ["lbl_password"]   = "Passwort",
            ["btn_save"]       = "Speichern",
            ["lbl_traffic"]    = "Datenverkehr:",
            ["opt_all_apps"]   = "Alle Apps",
            ["opt_selected"]   = "Nur ausgewählt",
            ["btn_refresh"]    = "↻  Liste aktualisieren",
            ["sel_count"]      = "Ausgewählt: {0} von {1}",
            ["lbl_log"]        = "Ereignisprotokoll",
            ["btn_clear"]      = "Löschen",
            ["lbl_transferred"]= "Übertragen: ",
            ["lbl_table"]      = "Verbindungen: ",
            ["lbl_process"]    = "Prozess",
            ["lbl_path"]       = "Pfad",
            ["btn_settings"]   = "⚙",
            ["settings_title"] = "Einstellungen",
            ["settings_lang"]  = "Sprache",
            ["settings_theme"] = "Farbschema",
            ["btn_close"]      = "Schließen",
            ["theme_dark"]     = "Dunkel",
            ["theme_light"]    = "Hell",
            ["theme_blue"]     = "Dunkelblau",
            ["theme_green"]    = "Dunkelgrün",
            ["theme_purple"]   = "Dunkellila",
            ["lbl_saved_servers"] = "Gespeicherte Server",
            ["lbl_search"]     = "Suchen...",
        },
        ["es"] = new()
        {
            ["app_subtitle"]   = "proxy del sistema para Windows",
            ["status_active"]  = "ACTIVO",
            ["status_stopped"] = "DETENIDO",
            ["btn_start"]      = "▶  INICIAR",
            ["btn_stop"]       = "■  DETENER",
            ["lbl_type"]       = "Tipo",
            ["lbl_host"]       = "Dirección del servidor",
            ["lbl_port"]       = "Puerto",
            ["lbl_login"]      = "Usuario (opcional)",
            ["lbl_password"]   = "Contraseña",
            ["btn_save"]       = "Guardar",
            ["lbl_traffic"]    = "Tráfico:",
            ["opt_all_apps"]   = "Todas las apps",
            ["opt_selected"]   = "Solo seleccionados",
            ["btn_refresh"]    = "↻  Actualizar lista",
            ["sel_count"]      = "Seleccionados: {0} de {1}",
            ["lbl_log"]        = "Registro de eventos",
            ["btn_clear"]      = "Limpiar",
            ["lbl_transferred"]= "Transferido: ",
            ["lbl_table"]      = "Conexiones: ",
            ["lbl_process"]    = "Proceso",
            ["lbl_path"]       = "Ruta",
            ["btn_settings"]   = "⚙",
            ["settings_title"] = "Configuración",
            ["settings_lang"]  = "Idioma",
            ["settings_theme"] = "Esquema de color",
            ["btn_close"]      = "Cerrar",
            ["theme_dark"]     = "Oscuro",
            ["theme_light"]    = "Claro",
            ["theme_blue"]     = "Azul oscuro",
            ["theme_green"]    = "Verde oscuro",
            ["theme_purple"]   = "Púrpura oscuro",
            ["lbl_saved_servers"] = "Servidores",
            ["lbl_search"]     = "Buscar...",
        },
        ["fr"] = new()
        {
            ["app_subtitle"]   = "proxy système pour Windows",
            ["status_active"]  = "ACTIF",
            ["status_stopped"] = "ARRÊTÉ",
            ["btn_start"]      = "▶  DÉMARRER",
            ["btn_stop"]       = "■  ARRÊTER",
            ["lbl_type"]       = "Type",
            ["lbl_host"]       = "Adresse du serveur",
            ["lbl_port"]       = "Port",
            ["lbl_login"]      = "Identifiant (optionnel)",
            ["lbl_password"]   = "Mot de passe",
            ["btn_save"]       = "Enregistrer",
            ["lbl_traffic"]    = "Trafic:",
            ["opt_all_apps"]   = "Toutes les apps",
            ["opt_selected"]   = "Sélectionnés seulement",
            ["btn_refresh"]    = "↻  Actualiser la liste",
            ["sel_count"]      = "Sélectionnés: {0} sur {1}",
            ["lbl_log"]        = "Journal des événements",
            ["btn_clear"]      = "Effacer",
            ["lbl_transferred"]= "Transféré: ",
            ["lbl_table"]      = "Connexions: ",
            ["lbl_process"]    = "Processus",
            ["lbl_path"]       = "Chemin",
            ["btn_settings"]   = "⚙",
            ["settings_title"] = "Paramètres",
            ["settings_lang"]  = "Langue",
            ["settings_theme"] = "Jeu de couleurs",
            ["btn_close"]      = "Fermer",
            ["theme_dark"]     = "Sombre",
            ["theme_light"]    = "Clair",
            ["theme_blue"]     = "Bleu foncé",
            ["theme_green"]    = "Vert foncé",
            ["theme_purple"]   = "Violet foncé",
            ["lbl_saved_servers"] = "Serveurs",
            ["lbl_search"]     = "Rechercher...",
        },
        ["it"] = new()
        {
            ["app_subtitle"]   = "proxy di sistema per Windows",
            ["status_active"]  = "ATTIVO",
            ["status_stopped"] = "FERMATO",
            ["btn_start"]      = "▶  AVVIA",
            ["btn_stop"]       = "■  FERMA",
            ["lbl_type"]       = "Tipo",
            ["lbl_host"]       = "Indirizzo server",
            ["lbl_port"]       = "Porta",
            ["lbl_login"]      = "Login (opzionale)",
            ["lbl_password"]   = "Password",
            ["btn_save"]       = "Salva",
            ["lbl_traffic"]    = "Traffico:",
            ["opt_all_apps"]   = "Tutte le app",
            ["opt_selected"]   = "Solo selezionati",
            ["btn_refresh"]    = "↻  Aggiorna lista",
            ["sel_count"]      = "Selezionati: {0} di {1}",
            ["lbl_log"]        = "Registro eventi",
            ["btn_clear"]      = "Cancella",
            ["lbl_transferred"]= "Trasferito: ",
            ["lbl_table"]      = "Connessioni: ",
            ["lbl_process"]    = "Processo",
            ["lbl_path"]       = "Percorso",
            ["btn_settings"]   = "⚙",
            ["settings_title"] = "Impostazioni",
            ["settings_lang"]  = "Lingua",
            ["settings_theme"] = "Schema colori",
            ["btn_close"]      = "Chiudi",
            ["theme_dark"]     = "Scuro",
            ["theme_light"]    = "Chiaro",
            ["theme_blue"]     = "Blu scuro",
            ["theme_green"]    = "Verde scuro",
            ["theme_purple"]   = "Viola scuro",
            ["lbl_saved_servers"] = "Server",
            ["lbl_search"]     = "Cerca...",
        },
        ["zh"] = new()
        {
            ["app_subtitle"]   = "Windows 系统代理",
            ["status_active"]  = "运行中",
            ["status_stopped"] = "已停止",
            ["btn_start"]      = "▶  启动",
            ["btn_stop"]       = "■  停止",
            ["lbl_type"]       = "类型",
            ["lbl_host"]       = "服务器地址",
            ["lbl_port"]       = "端口",
            ["lbl_login"]      = "用户名（可选）",
            ["lbl_password"]   = "密码",
            ["btn_save"]       = "保存",
            ["lbl_traffic"]    = "流量:",
            ["opt_all_apps"]   = "所有应用",
            ["opt_selected"]   = "仅选定",
            ["btn_refresh"]    = "↻  刷新列表",
            ["sel_count"]      = "已选: {0} / {1}",
            ["lbl_log"]        = "事件日志",
            ["btn_clear"]      = "清空",
            ["lbl_transferred"]= "已传输: ",
            ["lbl_table"]      = "连接数: ",
            ["lbl_process"]    = "进程",
            ["lbl_path"]       = "路径",
            ["btn_settings"]   = "⚙",
            ["settings_title"] = "设置",
            ["settings_lang"]  = "语言",
            ["settings_theme"] = "颜色方案",
            ["btn_close"]      = "关闭",
            ["theme_dark"]     = "深色",
            ["theme_light"]    = "浅色",
            ["theme_blue"]     = "深蓝色",
            ["theme_green"]    = "深绿色",
            ["theme_purple"]   = "深紫色",
            ["lbl_saved_servers"] = "服务器",
            ["lbl_search"]     = "搜索...",
        },
    };
}
