import pandas as pd
import re
from scipy import stats

print("--- 1. LOADING GUESS AND HORROR LEVEL DATA ---")

POST_FILE = "User Study Post.csv"

mapeo_likert = {
    "Strongly Disagree": 1,
    "Disagree": 2,
    "Somewhat Disagree": 3,
    "Neither Agree nor Disagree": 4,
    "Somewhat Agree": 5,
    "Agree": 6,
    "Strongly Agree": 7,
}

# The user's professor noted that GUESS Q6 should be reversed.
REVERSE_ENJOY_PLAYING_ITEM = True


def read_form(path):
    """Read Google Forms CSV. The user's current file is comma-separated."""
    return pd.read_csv(path, sep=",", encoding="utf-8-sig", engine="python")


def normalizar_nickname(nombre):
    if pd.isna(nombre):
        return ""
    nombre_limpio = re.sub(r"[\s\-_]", "", str(nombre).lower())
    correcciones = {
        "haelañ": "haekal",
    }
    return correcciones.get(nombre_limpio, nombre_limpio)


def convertir_a_numero(val):
    if pd.isna(val) or val == "":
        return float("nan")
    val_str = str(val).strip()
    if val_str in mapeo_likert:
        return mapeo_likert[val_str]
    try:
        return float(val_str)
    except ValueError:
        return float("nan")


def find_col(df, keywords, exact=None):
    if exact and exact in df.columns:
        return exact
    keys = [k.lower() for k in keywords]
    matches = [c for c in df.columns if all(k in c.lower() for k in keys)]
    if not matches:
        raise KeyError(f"Cannot find column with keywords: {keywords}")
    return matches[0]


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


df_final = read_form(POST_FILE)

id_col = find_col(df_final, ["nickname", "whole", "user", "study"], exact="Nickname for the whole user study")
version_col = find_col(df_final, ["version"], exact="Which Version did you play?")

df_final["ID_Norm"] = df_final[id_col].apply(normalizar_nickname)
df_final["ID"] = df_final[id_col].astype(str).str.strip()
df_final["Grupo_Build"] = df_final[version_col].astype(str).str.strip()

# ---------------------------------------------------------
# IDENTIFY GUESS COLUMNS
# ---------------------------------------------------------
guess_engr_keywords = ["Whenever I stopped playing", "getting tired", "check events"]
guess_enjoy_keywords = ["game is fun", "play this game again", "enjoy playing"]
guess_pgrat_keywords = ["feel successful", "do as well as possible", "focused on my own performance"]

col_engr = [c for c in df_final.columns if any(k in c for k in guess_engr_keywords)]
col_enjoy = [c for c in df_final.columns if any(k in c for k in guess_enjoy_keywords)]
col_pgrat = [c for c in df_final.columns if any(k in c for k in guess_pgrat_keywords)]

# The user's file calls this "How was the horror level?" rather than annoyance.
col_horror = next((c for c in df_final.columns if "horror level" in c.lower()), None)
if col_horror is None:
    # fallback for the old script's wording
    col_horror = next((c for c in df_final.columns if "annoyance" in c.lower()), None)

if len(col_engr) != 3:
    print(f"⚠️ Expected 3 GUESS Engrossment columns, found {len(col_engr)}")
if len(col_enjoy) != 3:
    print(f"⚠️ Expected 3 GUESS Enjoyment columns, found {len(col_enjoy)}")
if len(col_pgrat) != 3:
    print(f"⚠️ Expected 3 GUESS Personal Gratification columns, found {len(col_pgrat)}")

# Convert GUESS columns to numeric.
for c in col_engr + col_enjoy + col_pgrat:
    df_final[c] = df_final[c].apply(convertir_a_numero)


# Convert horror level.
if col_horror:
    df_final["Horror_Level"] = df_final[col_horror].apply(convertir_a_numero)
else:
    print("⚠️ Could not find horror-level / annoyance column.")
    df_final["Horror_Level"] = float("nan")

# ---------------------------------------------------------
# CALCULATE SCORES
# skipna=False means if one item is missing, the subscale becomes NaN.
# ---------------------------------------------------------
df_final["GUESS_Engrossment"] = df_final[col_engr].mean(axis=1, skipna=False)
df_final["GUESS_Enjoyment"] = df_final[col_enjoy].mean(axis=1, skipna=False)
df_final["GUESS_Personal_Gratification"] = df_final[col_pgrat].mean(axis=1, skipna=False)

# Individual tables.
df_guess_ind = df_final[[
    "ID_Norm", "ID", "Grupo_Build",
    "GUESS_Engrossment", "GUESS_Enjoyment", "GUESS_Personal_Gratification",
]].copy()

df_horror_ind = df_final[["ID_Norm", "ID", "Grupo_Build", "Horror_Level"]].copy()

print(f"✅ Data scored for {len(df_final)} post-game users.")
print(df_final["Grupo_Build"].value_counts().to_string())

# ---------------------------------------------------------
# STATISTICAL ANALYSIS
# ---------------------------------------------------------
print("\n--- 2. STATISTICAL ANALYSIS ---")
grupos = sorted(df_final["Grupo_Build"].dropna().unique())

if len(grupos) != 2:
    print("⚠️ Warning: Need exactly two groups to perform the analysis.")
    df_res_guess = pd.DataFrame()
    df_res_horror = pd.DataFrame()
else:
    gA_name, gB_name = grupos[0], grupos[1]
    print(f"Comparing {gA_name} vs {gB_name}")

    def analyze_metric(df, metrica, name):
        df_clean = df.dropna(subset=[metrica])
        gA = df_clean[df_clean["Grupo_Build"] == gA_name][metrica]
        gB = df_clean[df_clean["Grupo_Build"] == gB_name][metrica]

        if len(gA) < 3 or len(gB) < 3:
            return None

        mean_A, sd_A = gA.mean(), gA.std()
        mean_B, sd_B = gB.mean(), gB.std()
        p_val = safe_mannwhitney(gA, gB)

        return {
            "Metric": name,
            f"Mean_{gA_name}": round(mean_A, 2),
            f"SD_{gA_name}": round(sd_A, 2),
            f"Mean_{gB_name}": round(mean_B, 2),
            f"SD_{gB_name}": round(sd_B, 2),
            "p_value": round(p_val, 4) if pd.notna(p_val) else p_val,
        }

    resultados_guess = []
    for m_col, m_name in [
        ("GUESS_Engrossment", "Player Engrossment"),
        ("GUESS_Enjoyment", "Enjoyment"),
        ("GUESS_Personal_Gratification", "Personal Gratification"),
    ]:
        res = analyze_metric(df_final, m_col, m_name)
        if res:
            resultados_guess.append(res)

    resultados_horror = []
    res_horror = analyze_metric(df_final, "Horror_Level", "Horror Level")
    if res_horror:
        resultados_horror.append(res_horror)

    df_res_guess = pd.DataFrame(resultados_guess)
    df_res_horror = pd.DataFrame(resultados_horror)

# ---------------------------------------------------------
# EXPORT
# ---------------------------------------------------------
df_guess_ind.to_csv("guess_individual_scores.csv", index=False, sep=",", decimal=".")
if not df_res_guess.empty:
    df_res_guess.to_csv("guess_statistical_analysis.csv", index=False, sep=",", decimal=".")

df_horror_ind.to_csv("horror_level_individual_scores.csv", index=False, sep=",", decimal=".")
if not df_res_horror.empty:
    df_res_horror.to_csv("horror_level_statistical_analysis.csv", index=False, sep=",", decimal=".")

# Also save old-style filenames for compatibility with the original script.
df_horror_ind.rename(columns={"Horror_Level": "Annoyance_Level"}).to_csv(
    "annoyance_individual_scores.csv", index=False, sep=",", decimal="."
)
if not df_res_horror.empty:
    df_res_horror.replace({"Horror Level": "Annoyance Level"}).to_csv(
        "annoyance_statistical_analysis.csv", index=False, sep=",", decimal="."
    )

print("\n✅ Files saved:")
print(" - guess_individual_scores.csv")
print(" - guess_statistical_analysis.csv")
print(" - horror_level_individual_scores.csv")
print(" - horror_level_statistical_analysis.csv")
print(" - annoyance_individual_scores.csv")
print(" - annoyance_statistical_analysis.csv")
