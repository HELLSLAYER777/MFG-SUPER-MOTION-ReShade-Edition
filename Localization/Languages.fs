namespace ARMO.Localization

type Language =
    { Code: string
      Name: string
      RightToLeft: bool
      FontFamily: string }

module Languages =
    let private latin code name = { Code = code; Name = name; RightToLeft = false; FontFamily = "Segoe UI" }
    let private script code name rtl = { Code = code; Name = name; RightToLeft = rtl; FontFamily = "Segoe UI" }

    // Indices are persisted. Keep existing entries in this order.
    let all =
        [| latin "en" "English"; script "ar" "العربية" true; latin "fr" "Français"
           latin "de" "Deutsch"; latin "es" "Español"; latin "it" "Italiano"
           latin "pt-BR" "Português (Brasil)"; script "ru" "Русский" false
           script "uk" "Українська" false; latin "pl" "Polski"; latin "nl" "Nederlands"
           latin "sv" "Svenska"; latin "da" "Dansk"; latin "nb" "Norsk"; latin "fi" "Suomi"
           latin "cs" "Čeština"; latin "sk" "Slovenčina"; latin "hu" "Magyar"
           latin "ro" "Română"; latin "el" "Ελληνικά"; latin "tr" "Türkçe"
           script "fa" "فارسی" true; script "he" "עברית" true; script "hi" "हिन्दी" false
           script "bn" "বাংলা" false; script "zh-Hans" "简体中文" false
           script "zh-Hant" "繁體中文" false; script "ja" "日本語" false
           script "ko" "한국어" false; latin "id" "Bahasa Indonesia" |]

    let count = all.Length
    let at index = all[if index >= 0 && index < count then index else 0]
    let names = all |> Array.map _.Name
