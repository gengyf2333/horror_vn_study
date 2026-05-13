import pandas as pd
import re
from scipy import stats

print("--- 1. LOADING AND SCORING PANAS DATA ---")

PRE_FILE = "User Study Pre.csv"
POST_FILE = "User Study Post.csv"

mapeo_likert = {
    "Very slightly or not at all": 1,
    "A little": 2,
    "Moderately": 3,
    "Quite a bit": 4,
    "Extremely": 5,
}

pa_emotions = [
    "Interested", "Excited", "Strong", "Enthusiastic", "Proud",
    "Alert", "Inspired", "Determined", "Attentive", "Active",
]

na_emotions = [
    "Distressed", "Upset", "Guilty", "Scared", "Hostile",
    "Irritable", "Ashamed", "Nervous", "Jittery", "Afraid",
]


def read_form(path):
    """Read Google Forms CSV. The user's current files are comma-separated."""
    return pd.read_csv(path, sep=",", encoding="utf-8-sig", engine="python")


def normalizar_nickname(nombre):
    if pd.isna(nombre):
        return ""
    nombre_limpio = re.sub(r"[\s\-_]", "", str(nombre).lower())
    correcciones = {
        "haelañ": "haekal",
    }
    return correcciones.get(nombre_limpio, nombre_limpio)


def convertir_panas(val):
    if pd.isna(val) or val == "":
        return float("nan")
    return mapeo_likert.get(str(val).strip(), float("nan"))


def find_col(df, keywords, exact=None):
    """Find a column by exact name or by all keywords."""
    if exact and exact in df.columns:
        return exact
    keys = [k.lower() for k in keywords]
    matches = [c for c in df.columns if all(k in c.lower() for k in keys)]
    if not matches:
        raise KeyError(f"Cannot find column with keywords: {keywords}")
    return matches[0]


def find_panas_col(df, emotion):
    matches = [c for c in df.columns if f"[{emotion}]" in c]
    if len(matches) != 1:
        raise KeyError(f"Expected one PANAS column for {emotion}, found {len(matches)}: {matches}")
    return matches[0]


def score_panas_df(df, suffix):
    id_col = find_col(df, ["nickname", "whole", "user", "study"], exact="Nickname for the whole user study")
    version_col = find_col(df, ["version"])

    scored = pd.DataFrame()
    scored["ID_Norm"] = df[id_col].apply(normalizar_nickname)
    scored["ID"] = df[id_col].astype(str).str.strip()
    scored["Grupo_Build"] = df[version_col].astype(str).str.strip()

    pa_cols = []
    na_cols = []

    for emotion in pa_emotions + na_emotions:
        col = find_panas_col(df, emotion)
        out_col = f"{emotion}_{suffix}"
        scored[out_col] = df[col].apply(convertir_panas)
        if emotion in pa_emotions:
            pa_cols.append(out_col)
        else:
            na_cols.append(out_col)

    # Strict scoring: if one item is missing, the total becomes NaN.
    scored[f"PA_Total_{suffix}"] = scored[pa_cols].sum(axis=1, min_count=len(pa_cols))
    scored[f"NA_Total_{suffix}"] = scored[na_cols].sum(axis=1, min_count=len(na_cols))

    return scored[["ID_Norm", "ID", "Grupo_Build", f"PA_Total_{suffix}", f"NA_Total_{suffix}"]]


def safe_wilcoxon(pre, post):
    data = pd.DataFrame({"pre": pre, "post": post}).dropna()
    if len(data) < 3:
        return float("nan")
    if (data["pre"].values == data["post"].values).all():
        return 1.0
    try:
        _, p = stats.wilcoxon(data["pre"], data["post"])
        return p
    except ValueError:
        return float("nan")


def safe_mannwhitney(a, b):
    a = pd.Series(a).dropna()
    b = pd.Series(b).dropna()
    if len(a) < 3 or len(b) < 3:
        return float("nan")
    try:
        _, p = stats.mannwhitneyu(a, b, alternative="two-sided")
        return p
    except ValueError:
        return float("nan")


pre = read_form(PRE_FILE)
post = read_form(POST_FILE)

pre_scored = score_panas_df(pre, "pre")
post_scored = score_panas_df(post, "post")

# Keep only participants with both pre and post.
df_final = pd.merge(
    pre_scored,
    post_scored,
    on="ID_Norm",
    suffixes=("_pre_file", "_post_file"),
    how="inner",
)

# Use pre version as the grouping variable, and warn if it differs from post.
df_final["Grupo_Build"] = df_final["Grupo_Build_pre_file"]
mismatch = df_final[df_final["Grupo_Build_pre_file"] != df_final["Grupo_Build_post_file"]]
if not mismatch.empty:
    print("⚠️ Version mismatch found:")
    print(mismatch[["ID_pre_file", "Grupo_Build_pre_file", "Grupo_Build_post_file"]])

# Clean columns for output.
df_final["ID"] = df_final["ID_pre_file"]
df_final = df_final[[
    "ID_Norm", "ID", "Grupo_Build",
    "PA_Total_pre", "NA_Total_pre", "PA_Total_post", "NA_Total_post",
]]

print(f"✅ PANAS scored and merged for {len(df_final)} users.")
print(df_final["Grupo_Build"].value_counts().to_string())

print("\n--- 2. PANAS STATISTICAL ANALYSIS ---")
grupos = sorted(df_final["Grupo_Build"].dropna().unique())

if len(grupos) != 2:
    print("⚠️ Warning: Need exactly two groups to perform the analysis.")
    resultados = []
else:
    gA_name, gB_name = grupos[0], grupos[1]
    print(f"Comparing {gA_name} vs {gB_name}")
    resultados = []

    for metrica, nombre in [("PA_Total", "Positive Affect (PA)"), ("NA_Total", "Negative Affect (NA)")]:
        df_metrica = df_final[["ID_Norm", "Grupo_Build", f"{metrica}_pre", f"{metrica}_post"]].copy()
        df_limpio = df_metrica.dropna(subset=[f"{metrica}_pre", f"{metrica}_post"])

        gA = df_limpio[df_limpio["Grupo_Build"] == gA_name]
        gB = df_limpio[df_limpio["Grupo_Build"] == gB_name]

        data_A_pre, data_A_post = gA[f"{metrica}_pre"], gA[f"{metrica}_post"]
        data_B_pre, data_B_post = gB[f"{metrica}_pre"], gB[f"{metrica}_post"]

        if len(data_A_pre) < 3 or len(data_B_pre) < 3:
            print(f"ℹ️ Not enough valid data to process {metrica}.")
            continue

        mean_A_pre, sd_A_pre = data_A_pre.mean(), data_A_pre.std()
        mean_A_post, sd_A_post = data_A_post.mean(), data_A_post.std()
        mean_B_pre, sd_B_pre = data_B_pre.mean(), data_B_pre.std()
        mean_B_post, sd_B_post = data_B_post.mean(), data_B_post.std()

        p_baseline = safe_mannwhitney(data_A_pre, data_B_pre)
        p_wilc_A = safe_wilcoxon(data_A_pre, data_A_post)
        p_wilc_B = safe_wilcoxon(data_B_pre, data_B_post)
        p_final = safe_mannwhitney(data_A_post, data_B_post)

        resultados.append({
            "Metric": nombre,
            "Mean_A_Pre": round(mean_A_pre, 2), "SD_A_Pre": round(sd_A_pre, 2),
            "Mean_A_Post": round(mean_A_post, 2), "SD_A_Post": round(sd_A_post, 2),
            "Mean_B_Pre": round(mean_B_pre, 2), "SD_B_Pre": round(sd_B_pre, 2),
            "Mean_B_Post": round(mean_B_post, 2), "SD_B_Post": round(sd_B_post, 2),
            "p_Baseline": round(p_baseline, 4) if pd.notna(p_baseline) else p_baseline,
            "p_Interno_A": round(p_wilc_A, 4) if pd.notna(p_wilc_A) else p_wilc_A,
            "p_Interno_B": round(p_wilc_B, 4) if pd.notna(p_wilc_B) else p_wilc_B,
            "p_Final": round(p_final, 4) if pd.notna(p_final) else p_final,
        })

df_resumen = pd.DataFrame(resultados)

print("\n--- PANAS SUMMARY ---")
if not df_resumen.empty:
    print(df_resumen[["Metric", "p_Baseline", "p_Final"]].to_string(index=False))
    df_final.to_csv("panas_individual_scores.csv", index=False, sep=",", decimal=".")
    df_resumen.to_csv("panas_statistical_analysis.csv", index=False, sep=",", decimal=".")
    print("\n✅ Files saved:")
    print(" - panas_individual_scores.csv")
    print(" - panas_statistical_analysis.csv")
else:
    print("❌ No metrics could be calculated.")
