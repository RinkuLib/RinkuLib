using System.Globalization;

namespace Rinku.More.Globalization;

/// <summary>
/// Common cultures used by the globalization helpers.
/// </summary>
public static class C
{
    public static readonly CultureInfo INV = CultureInfo.InvariantCulture;

    private static CultureInfo Get(string name) => CultureInfo.GetCultureInfo(name);

    #region Principal Cultures

    public static readonly CultureInfo EN = Get("en");
    public static readonly CultureInfo FR = Get("fr");
    public static readonly CultureInfo ES = Get("es");
    public static readonly CultureInfo DE = Get("de");
    public static readonly CultureInfo IT = Get("it");
    public static readonly CultureInfo PT = Get("pt");
    public static readonly CultureInfo RU = Get("ru");
    public static readonly CultureInfo ZH = Get("zh");
    public static readonly CultureInfo JA = Get("ja");
    public static readonly CultureInfo KO = Get("ko");
    public static readonly CultureInfo AR = Get("ar");
    public static readonly CultureInfo NL = Get("nl");
    public static readonly CultureInfo SV = Get("sv");
    public static readonly CultureInfo DA = Get("da");
    public static readonly CultureInfo NO = Get("no");
    public static readonly CultureInfo FI = Get("fi");
    public static readonly CultureInfo TR = Get("tr");
    public static readonly CultureInfo PL = Get("pl");
    public static readonly CultureInfo CS = Get("cs");
    public static readonly CultureInfo HU = Get("hu");
    public static readonly CultureInfo TH = Get("th");
    public static readonly CultureInfo VI = Get("vi");
    public static readonly CultureInfo EL = Get("el");
    public static readonly CultureInfo HI = Get("hi");
    public static readonly CultureInfo BN = Get("bn");
    public static readonly CultureInfo ID = Get("id");
    public static readonly CultureInfo MS = Get("ms");
    public static readonly CultureInfo SR = Get("sr");
    public static readonly CultureInfo RO = Get("ro");
    public static readonly CultureInfo SK = Get("sk");
    public static readonly CultureInfo LT = Get("lt");
    public static readonly CultureInfo LV = Get("lv");
    public static readonly CultureInfo ET = Get("et");
    public static readonly CultureInfo SL = Get("sl");
    public static readonly CultureInfo IS = Get("is");
    public static readonly CultureInfo MT = Get("mt");

    #endregion

    #region Variants

    public static readonly CultureInfo EN_US = Get("en-US");
    public static readonly CultureInfo EN_GB = Get("en-GB");
    public static readonly CultureInfo FR_FR = Get("fr-FR");
    public static readonly CultureInfo FR_CA = Get("fr-CA");
    public static readonly CultureInfo ES_ES = Get("es-ES");
    public static readonly CultureInfo ES_MX = Get("es-MX");
    public static readonly CultureInfo DE_DE = Get("de-DE");
    public static readonly CultureInfo IT_IT = Get("it-IT");
    public static readonly CultureInfo PT_BR = Get("pt-BR");
    public static readonly CultureInfo RU_RU = Get("ru-RU");
    public static readonly CultureInfo ZH_CN = Get("zh-CN");
    public static readonly CultureInfo ZH_TW = Get("zh-TW");
    public static readonly CultureInfo JA_JP = Get("ja-JP");
    public static readonly CultureInfo KO_KR = Get("ko-KR");
    public static readonly CultureInfo AR_SA = Get("ar-SA");
    public static readonly CultureInfo NL_NL = Get("nl-NL");
    public static readonly CultureInfo SV_SE = Get("sv-SE");
    public static readonly CultureInfo DA_DK = Get("da-DK");
    public static readonly CultureInfo NO_NO = Get("no-NO");
    public static readonly CultureInfo FI_FI = Get("fi-FI");
    public static readonly CultureInfo TR_TR = Get("tr-TR");
    public static readonly CultureInfo PL_PL = Get("pl-PL");
    public static readonly CultureInfo CS_CZ = Get("cs-CZ");
    public static readonly CultureInfo HU_HU = Get("hu-HU");
    public static readonly CultureInfo TH_TH = Get("th-TH");
    public static readonly CultureInfo VI_VN = Get("vi-VN");
    public static readonly CultureInfo EL_GR = Get("el-GR");
    public static readonly CultureInfo HI_IN = Get("hi-IN");
    public static readonly CultureInfo BN_BD = Get("bn-BD");
    public static readonly CultureInfo ID_ID = Get("id-ID");
    public static readonly CultureInfo MS_MY = Get("ms-MY");
    public static readonly CultureInfo SR_RS = Get("sr-RS");
    public static readonly CultureInfo RO_RO = Get("ro-RO");
    public static readonly CultureInfo SK_SK = Get("sk-SK");
    public static readonly CultureInfo LT_LT = Get("lt-LT");
    public static readonly CultureInfo LV_LV = Get("lv-LV");
    public static readonly CultureInfo ET_EE = Get("et-EE");
    public static readonly CultureInfo SL_SI = Get("sl-SI");
    public static readonly CultureInfo IS_IS = Get("is-IS");
    public static readonly CultureInfo MT_MT = Get("mt-MT");

    #endregion
}
