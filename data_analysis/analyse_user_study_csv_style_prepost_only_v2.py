import os
import re
import numpy as np
import pandas as pd
from scipy import stats

# ============================================================
# CONFIG
# ============================================================
PRE_FILE = "User Study Pre.csv"
POST_FILE = "User Study Post.csv"
OUT_SUMMARY = "complete_statistical_analysis.csv"
OUT_SCORE_TABLE = "score_table.csv"
OUT_VERSION_TABLE = "participant_version_table.csv"

# Set to True if you want to exclude participants manually.
EXCLUDED_IDS = []

# ============================================================
# AUXILIARY FUNCTIONS
# ============================================================
def normalize_nickname(name):
    """Normalize participant nicknames for matching pre and post forms."""
    if pd.isna(name):
        return ""
    name_clean = re.sub(r"[\s\-_]", "", str(name).lower())
    corrections = {
        # Add manual corrections here if needed, for example:
        # "lukea": "lukea",
    }
    return corrections.get(name_clean, name_clean)


def clean_columns(df):
    df = df.copy()
    df.columns = [str(c).strip() for c in df.columns]
    return df


def find_col(df, candidates, required=True):
    """Find a column by exact or fuzzy candidate names."""
    cols = list(df.columns)
    lowered = {c.lower().strip(): c for c in cols}

    for cand in candidates:
        key = cand.lower().strip()
        if key in lowered:
            return lowered[key]

    for cand in candidates:
        key = cand.lower().strip()
        for c in cols:
            if key in c.lower().strip():
                return c

    if required:
        raise KeyError(f"Cannot find column. Candidates: {candidates}. Available columns: {cols}")
    return None


LIKERT_MAP = {
    "very slightly or not at all": 1,
    "very slight": 1,
    "very slightly": 1,
    "a little": 2,
    "moderately": 3,
    "quite a bit": 4,
    "extremely": 5,
    "strongly disagree": 1,
    "disagree": 2,
    "somewhat disagree": 3,
    "neither agree nor disagree": 4,
    "somewhat agree": 5,
    "agree": 6,
    "strongly agree": 7,
}


def to_number(value):
    """Convert Google Forms text responses to numeric values."""
    if pd.isna(value) or value == "":
        return np.nan
    value_str = str(value).strip()
    key = value_str.lower()
    if key in LIKERT_MAP:
        return LIKERT_MAP[key]
    try:
        return float(value_str)
    except ValueError:
        return np.nan


def extract_item_name(col_name):
    """Extract item name from square brackets, e.g. [Interested]."""
    match = re.search(r"\[(.*?)\]", str(col_name))
    if match:
        return match.group(1).strip()
    return str(col_name).strip()


def find_panas_columns(df):
    """Return dict {PANAS item: column name}."""
    out = {}
    for c in df.columns:
        item = extract_item_name(c)
        if item in PANAS_ITEMS:
            out[item] = c
    missing = [item for item in PANAS_ITEMS if item not in out]
    if missing:
        raise KeyError(f"Missing PANAS columns: {missing}")
    return out


def find_sam_columns(df):
    """Find the three Manikin/SAM columns. Handles duplicated names and extra spaces."""
    sam_cols = [
        c for c in df.columns
        if "select to what extent" in c.lower()
        and "represented" in c.lower()
        and "image" in c.lower()
    ]
    if len(sam_cols) != 3:
        raise ValueError(f"Expected 3 SAM columns. Found {len(sam_cols)}: {sam_cols}")
    return sam_cols


def safe_wilcoxon(pre_values, post_values):
    data = pd.DataFrame({"pre": pre_values, "post": post_values}).dropna()
    if len(data) < 2:
        return np.nan, np.nan
    if np.allclose(data["pre"].values, data["post"].values):
        return 0.0, 1.0
    try:
        stat, p = stats.wilcoxon(data["pre"], data["post"], alternative="two-sided", zero_method="wilcox")
        return stat, p
    except ValueError:
        return np.nan, np.nan


def safe_mannwhitney(values_a, values_b):
    a = pd.Series(values_a).dropna()
    b = pd.Series(values_b).dropna()
    if len(a) < 2 or len(b) < 2:
        return np.nan, np.nan
    try:
        stat, p = stats.mannwhitneyu(a, b, alternative="two-sided")
        return stat, p
    except ValueError:
        return np.nan, np.nan


def mean_sd(values):
    s = pd.Series(values).dropna()
    if len(s) == 0:
        return np.nan, np.nan
    return round(s.mean(), 2), round(s.std(ddof=1), 2) if len(s) > 1 else np.nan


def round_p(p):
    return round(float(p), 4) if not pd.isna(p) else np.nan

# ============================================================
# SCALE DEFINITIONS
# ============================================================
PA_ITEMS = [
    "Interested", "Excited", "Strong", "Enthusiastic", "Proud",
    "Alert", "Inspired", "Determined", "Attentive", "Active"
]

NA_ITEMS = [
    "Distressed", "Upset", "Guilty", "Scared", "Hostile",
    "Irritable", "Ashamed", "Nervous", "Jittery", "Afraid"
]

PANAS_ITEMS = PA_ITEMS + NA_ITEMS

# ============================================================
# 1. LOAD FORMS
# ============================================================
print("--- 1. LOADING FORMS ---")

# IMPORTANT: force comma separator. Do NOT use sep=None here.
# The Post CSV has long quoted GUESS headers, so automatic separator detection may fail.
df_pre = pd.read_csv(PRE_FILE, sep=",", encoding="utf-8-sig", engine="python")
df_post = pd.read_csv(POST_FILE, sep=",", encoding="utf-8-sig", engine="python")

df_pre = clean_columns(df_pre)
df_post = clean_columns(df_post)

pre_id_col = find_col(df_pre, ["Nickname for the whole user study", "Nickname"])
post_id_col = find_col(df_post, ["Nickname for the whole user study", "Nickname"])
pre_version_col = find_col(df_pre, ["Which Version do you play?"])
post_version_col = find_col(df_post, ["Which Version did you play?"])

print(f"Pre rows: {len(df_pre)}, Post rows: {len(df_post)}")
print(f"Pre ID column: {pre_id_col}")
print(f"Post ID column: {post_id_col}")
print(f"Pre version column: {pre_version_col}")
print(f"Post version column: {post_version_col}")

# Normalize IDs and remove excluded users if any.
df_pre["ID_Norm"] = df_pre[pre_id_col].apply(normalize_nickname)
df_post["ID_Norm"] = df_post[post_id_col].apply(normalize_nickname)

# Remove blank IDs before matching.
df_pre = df_pre[df_pre["ID_Norm"] != ""].copy()
df_post = df_post[df_post["ID_Norm"] != ""].copy()

excluded_norm = [normalize_nickname(x) for x in EXCLUDED_IDS]
if excluded_norm:
    df_pre = df_pre[~df_pre["ID_Norm"].isin(excluded_norm)]
    df_post = df_post[~df_post["ID_Norm"].isin(excluded_norm)]

# Detective mode: show unmatched IDs.
ids_pre = set(df_pre["ID_Norm"])
ids_post = set(df_post["ID_Norm"])
pre_only = sorted(ids_pre - ids_post)
post_only = sorted(ids_post - ids_pre)
if pre_only:
    print(f"⚠️ IDs in Pre but missing in Post: {pre_only}")
if post_only:
    print(f"⚠️ IDs in Post but missing in Pre: {post_only}")

# Use only paired participants for pre-post statistical analysis.
df_pre_small = df_pre[["ID_Norm", pre_id_col, pre_version_col]].rename(
    columns={pre_id_col: "ID", pre_version_col: "Version_Pre"}
)
df_post_small = df_post[["ID_Norm", post_id_col, post_version_col]].rename(
    columns={post_id_col: "ID_Post", post_version_col: "Version_Post"}
)

paired = pd.merge(df_pre_small, df_post_small, on="ID_Norm", how="inner")
paired["Version"] = paired["Version_Pre"].fillna(paired["Version_Post"])

mismatch = paired[paired["Version_Pre"] != paired["Version_Post"]]
if not mismatch.empty:
    print("⚠️ Version mismatch between Pre and Post:")
    print(mismatch[["ID", "Version_Pre", "Version_Post"]].to_string(index=False))

print(f"Successfully matched paired participants: {len(paired)}")
print("Participant counts by version:")
print(paired["Version"].value_counts().to_string())

# ============================================================
# 2. CALCULATE SCORES
# ============================================================
print("\n--- 2. CALCULATING SCORES ---")

pre_panas_cols = find_panas_columns(df_pre)
post_panas_cols = find_panas_columns(df_post)
pre_sam_cols = find_sam_columns(df_pre)
post_sam_cols = find_sam_columns(df_post)

score_rows = []

# Create lookup dictionaries for row access.
pre_by_id = {row["ID_Norm"]: row for _, row in df_pre.iterrows()}
post_by_id = {row["ID_Norm"]: row for _, row in df_post.iterrows()}

for _, person in paired.iterrows():
    uid = person["ID_Norm"]
    pre_row = pre_by_id[uid]
    post_row = post_by_id[uid]

    out = {
        "ID": person["ID"],
        "Version": person["Version"],
        "ID_Norm": uid,
    }

    # PANAS item scores, used only to calculate PA and NA.
    for item in PANAS_ITEMS:
        out[f"PANAS_{item}_Pre"] = to_number(pre_row[pre_panas_cols[item]])
        out[f"PANAS_{item}_Post"] = to_number(post_row[post_panas_cols[item]])

    pa_pre = [out[f"PANAS_{item}_Pre"] for item in PA_ITEMS]
    pa_post = [out[f"PANAS_{item}_Post"] for item in PA_ITEMS]
    na_pre = [out[f"PANAS_{item}_Pre"] for item in NA_ITEMS]
    na_post = [out[f"PANAS_{item}_Post"] for item in NA_ITEMS]

    # Require all 10 PA/NA items to be present.
    out["PANAS_PA_Pre"] = sum(pa_pre) if not any(pd.isna(x) for x in pa_pre) else np.nan
    out["PANAS_PA_Post"] = sum(pa_post) if not any(pd.isna(x) for x in pa_post) else np.nan
    out["PANAS_NA_Pre"] = sum(na_pre) if not any(pd.isna(x) for x in na_pre) else np.nan
    out["PANAS_NA_Post"] = sum(na_post) if not any(pd.isna(x) for x in na_post) else np.nan

    # SAM / Manikin:
    # 1) Valence: 1 = Happy, 5 = Unhappy -> already higher = more unpleasant
    # 2) Arousal: 1 = Excited, 5 = Calm -> reverse to higher = more aroused
    # 3) Control: 1 = Controlled, 5 = In Control -> reverse to higher = more loss of control
    pre_valence_raw = to_number(pre_row[pre_sam_cols[0]])
    pre_arousal_raw = to_number(pre_row[pre_sam_cols[1]])
    pre_control_raw = to_number(pre_row[pre_sam_cols[2]])
    post_valence_raw = to_number(post_row[post_sam_cols[0]])
    post_arousal_raw = to_number(post_row[post_sam_cols[1]])
    post_control_raw = to_number(post_row[post_sam_cols[2]])

    out["SAM_Unpleasantness_Pre"] = pre_valence_raw
    out["SAM_Unpleasantness_Post"] = post_valence_raw
    out["SAM_Arousal_Pre"] = 6 - pre_arousal_raw if not pd.isna(pre_arousal_raw) else np.nan
    out["SAM_Arousal_Post"] = 6 - post_arousal_raw if not pd.isna(post_arousal_raw) else np.nan
    out["SAM_LossControl_Pre"] = 6 - pre_control_raw if not pd.isna(pre_control_raw) else np.nan
    out["SAM_LossControl_Post"] = 6 - post_control_raw if not pd.isna(post_control_raw) else np.nan

    # GUESS post-only.
    guess_cols = [c for c in df_post.columns if c.lower().startswith("based on your experience")]
    guess_values = []
    for i, c in enumerate(guess_cols, start=1):
        value = to_number(post_row[c])
        out[f"GUESS_Q{i}_Post"] = value
        guess_values.append(value)

    out["GUESS_Total_Post"] = np.nanmean(guess_values) if len(guess_values) and not all(pd.isna(x) for x in guess_values) else np.nan

    horror_col = find_col(df_post, ["How was the horror level?"], required=False)
    out["Horror_Level_Post"] = to_number(post_row[horror_col]) if horror_col else np.nan

    score_rows.append(out)

score_df = pd.DataFrame(score_rows)

# Calculate change scores for pre-post metrics.
prepost_metrics = [
    "PANAS_PA",
    "PANAS_NA",
    "SAM_Unpleasantness",
    "SAM_Arousal",
    "SAM_LossControl",
]

postonly_metrics = ["GUESS_Total", "Horror_Level"]

for metric in prepost_metrics:
    score_df[f"{metric}_Change"] = score_df[f"{metric}_Post"] - score_df[f"{metric}_Pre"]

# ============================================================
# 3. STATISTICAL ANALYSIS
# ============================================================
print("\n--- 3. STATISTICAL ANALYSIS ---")

groups = list(score_df["Version"].dropna().unique())
groups = sorted(groups)
if len(groups) != 2:
    raise ValueError(f"Expected exactly 2 versions. Found {len(groups)}: {groups}")

gA_name, gB_name = groups[0], groups[1]
print(f"Analyzing {gA_name} vs {gB_name}")

results = []

# Pre-post metrics: PANAS PA/NA and SAM.
for metric in prepost_metrics:
    df_m = score_df[["ID", "Version", f"{metric}_Pre", f"{metric}_Post"]].copy()
    df_m = df_m.dropna(subset=[f"{metric}_Pre", f"{metric}_Post"])

    gA = df_m[df_m["Version"] == gA_name]
    gB = df_m[df_m["Version"] == gB_name]

    data_A_pre = gA[f"{metric}_Pre"]
    data_A_post = gA[f"{metric}_Post"]
    data_B_pre = gB[f"{metric}_Pre"]
    data_B_post = gB[f"{metric}_Post"]

    mean_A_pre, sd_A_pre = mean_sd(data_A_pre)
    mean_A_post, sd_A_post = mean_sd(data_A_post)
    mean_B_pre, sd_B_pre = mean_sd(data_B_pre)
    mean_B_post, sd_B_post = mean_sd(data_B_post)

    _, p_baseline = safe_mannwhitney(data_A_pre, data_B_pre)
    _, p_wilc_A = safe_wilcoxon(data_A_pre, data_A_post)
    _, p_wilc_B = safe_wilcoxon(data_B_pre, data_B_post)
    _, p_final = safe_mannwhitney(data_A_post, data_B_post)

    results.append({
        "Metric": metric,
        "Mean_A_Pre": mean_A_pre,
        "SD_A_Pre": sd_A_pre,
        "Mean_A_Post": mean_A_post,
        "SD_A_Post": sd_A_post,
        "Mean_B_Pre": mean_B_pre,
        "SD_B_Pre": sd_B_pre,
        "Mean_B_Post": mean_B_post,
        "SD_B_Post": sd_B_post,
        "p_Baseline": round_p(p_baseline),
        "p_Interno_A": round_p(p_wilc_A),
        "p_Interno_B": round_p(p_wilc_B),
        "p_Final": round_p(p_final),
    })

# Post-only metrics: GUESS total and horror level.
# Same CSV columns are kept. Pre/internal columns remain NaN.
for metric in postonly_metrics:
    col_post = f"{metric}_Post"
    if col_post not in score_df.columns:
        continue

    gA = score_df[score_df["Version"] == gA_name]
    gB = score_df[score_df["Version"] == gB_name]

    data_A_post = gA[col_post]
    data_B_post = gB[col_post]

    mean_A_post, sd_A_post = mean_sd(data_A_post)
    mean_B_post, sd_B_post = mean_sd(data_B_post)
    _, p_final = safe_mannwhitney(data_A_post, data_B_post)

    results.append({
        "Metric": metric,
        "Mean_A_Pre": np.nan,
        "SD_A_Pre": np.nan,
        "Mean_A_Post": mean_A_post,
        "SD_A_Post": sd_A_post,
        "Mean_B_Pre": np.nan,
        "SD_B_Pre": np.nan,
        "Mean_B_Post": mean_B_post,
        "SD_B_Post": sd_B_post,
        "p_Baseline": np.nan,
        "p_Interno_A": np.nan,
        "p_Interno_B": np.nan,
        "p_Final": round_p(p_final),
    })

summary_df = pd.DataFrame(results)

# ============================================================
# 4. EXPORT CSV FILES
# ============================================================
print("\n--- 4. EXPORTING CSV FILES ---")

# Participant-version table.
version_table = score_df[["ID", "Version"]].copy()
version_table.to_csv(OUT_VERSION_TABLE, index=False, encoding="utf-8-sig")

# Score table.
score_df.to_csv(OUT_SCORE_TABLE, index=False, encoding="utf-8-sig")

# Complete statistical analysis table.
summary_df.to_csv(OUT_SUMMARY, index=False, encoding="utf-8-sig")

# Split original pre/post by version, using only paired participants.
paired_ids = set(score_df["ID_Norm"])
pre_matched = df_pre[df_pre["ID_Norm"].isin(paired_ids)].copy()
post_matched = df_post[df_post["ID_Norm"].isin(paired_ids)].copy()

# Add clean Version column for easier checking.
pre_matched["Version"] = pre_matched[pre_version_col]
post_matched["Version"] = post_matched[post_version_col]

for version in groups:
    safe_name = str(version).replace(" ", "_").replace("/", "_")
    pre_out = pre_matched[pre_matched["Version"] == version].copy()
    post_out = post_matched[post_matched["Version"] == version].copy()

    pre_out.to_csv(f"{safe_name}_pre.csv", index=False, encoding="utf-8-sig")
    post_out.to_csv(f"{safe_name}_post.csv", index=False, encoding="utf-8-sig")

print("✅ All files saved successfully.")
print("Files generated:")
print(f"- {OUT_SUMMARY}")
print(f"- {OUT_SCORE_TABLE}")
print(f"- {OUT_VERSION_TABLE}")
for version in groups:
    safe_name = str(version).replace(" ", "_").replace("/", "_")
    print(f"- {safe_name}_pre.csv")
    print(f"- {safe_name}_post.csv")
