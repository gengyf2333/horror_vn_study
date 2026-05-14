# Horror Visual Novel Study Pipeline

This repository is bulit for the paper:Can LLMs Generate Horror Atmosphere in Visual Novels? A Preliminary Player-Affect Evaluation

This repository contains a simple pipeline for generating a visual novel story, converting it into a Unity-readable JSON format, generating visual assets, and analysing the user study data.

The project has two main parts:

1. **Generation pipeline**: creates the neutral and horrorified visual novel materials.
2. **Data analysis pipeline**: analyses the pre-game and post-game questionnaire results.

---



---

## 1. Folder Structure

A suggested folder structure is:

```text
project_root/
├── code/
│   ├── generate_story.py
│   ├── horrorify_story.py
│   ├── convert_story_to_json.py
│   ├── image_generate_list.py
│   ├── generate_image.py
├── prompt/
│   ├── neutral_novel.txt
│   ├── horror_novel.txt
│   ├── convert_json.txt
│   └── image_list.txt
├── data_analysis/
│   ├── Consent Form - Google Forms.pdf
│   ├── User Study Pre - Google Forms.pdf
│   ├── User Study Post - Google Forms.pdf
│   ├── User Study Pre.csv
│   ├── User Study Post.csv
│   ├── analyse_user_study_csv_style_prepost_only_v2.py
│   ├── guess_horror_prepost_only.py
│   └── panas_prepost_only.py
└── Horror_VN_Unity_project/

```
The `code/` folder stores all codes to generate story and visual novel related things.
The `prompt/` folder stores the prompt templates.  
The `data_analysis/` folder stores questionnaires, raw questionnaire results and code to analyse raw data.
The `Horror_VN_Unity_project/` folder stores the visual novel game powered by Unity.

---

## 2. Requirements

- **Unity Version**: Ensure you have **Unity 2022.3.62f1** installed. You can download it from the [Unity Hub](https://unity.com/download).


The generation scripts use the OpenAI API. Before running them, replace the placeholder API key in the scripts:

```python
OPENAI_API_KEY = "sk-proj-xxxxxxxx"
```

---


## 3. Generation Pipeline

### Step 1: Generate a neutral story

Run:

```bash
python code/generate_story.py \
  --prompt prompt/neutral_novel.txt \
  --out outputs/story_neutral_text.txt
```

This creates:

```text
outputs/story_neutral_text.txt
```

This file is a plain-text neutral visual novel story.

---

### Step 2: Generate a horrorified story

Run:

```bash
python code/horrorify_story.py \
  --prompt prompt/horror_novel.txt \
  --input outputs/story_neutral_text.txt \
  --out outputs/story_horrorified_text.txt
```

This creates:

```text
outputs/story_horrorified_text.txt
```

This file is a horrorified version of the neutral story. The main story structure and character consistency are preserved as much as possible.

---

### Step 3: Convert the stories into Unity-readable JSON

Convert the neutral story:

```bash
python code/convert_story_to_json.py \
  --prompt prompt/convert_json.txt \
  --input outputs/story_neutral_text.txt \
  --out outputs/story_neutral.json
```

Convert the horrorified story:

```bash
python code/convert_story_to_json.py \
  --prompt prompt/convert_json.txt \
  --input outputs/story_horrorified_text.txt \
  --out outputs/story_horrorified.json
```

This creates:

```text
outputs/story_neutral.json
outputs/story_horrorified.json
```

These JSON files can be loaded by the Unity visual novel framework.

---

### Step 4: Generate image prompt lists

Generate image prompts for the neutral version:

```bash
python code/image_generate_list.py \
  --prompt prompt/image_list.txt \
  --input outputs/story_neutral.json \
  --out outputs/asset_prompts_neutral.json \
  --mode neutral
```

Generate image prompts for the horrorified version:

```bash
python code/image_generate_list.py \
  --prompt prompt/image_list.txt \
  --input outputs/story_horrorified.json \
  --out outputs/asset_prompts_horrorified.json \
  --mode horrorified
```

This creates:

```text
outputs/asset_prompts_neutral.json
outputs/asset_prompts_horrorified.json
```

These files contain the image prompts for backgrounds and character sprites.

---

### Step 5: Generate images

Generate neutral images:

```bash
python code/generate_image.py \
  --input outputs/asset_prompts_neutral.json \
  --outdir outputs/images/neutral \
  --only all
```

Generate horrorified images:

```bash
python code/generate_image.py \
  --input outputs/asset_prompts_horrorified.json \
  --outdir outputs/images/horrorified \
  --only all
```

The generated images are saved in:

```text
outputs/images/neutral/
outputs/images/horrorified/
```

Background images are saved in the `backgrounds/` folder.  
Character sprites are saved in the `characters/` folder.

---

## 4. Using the Generated Files in Unity

Download `Horror_VN_Unity_project/` folder.

To use the generated materials in Unity:

1. Copy the generated story JSON file into the Unity story data folder, `Assets/StreamingAssets/pre-horror`.
2. Copy whole generated `horrorified` folder and `neutral` folder into the Unity background image folder, `Assets/Resources/pre-horror`.
3. Make sure the asset IDs in the JSON match the image filenames. It should be the same, but please double check for safety.

For example, if the JSON contains:

```json
"bg": "school_corridor_night",
"leftCharacter": "mira_neutral"
```

Then the image files should be named:

```text
school_corridor_night.png
mira_neutral.png
```

After opening the Unity project, go to `Assets/Scenes/AI-chat`, waiting for panel appear and you can play the demo. There will be two versions, version 1 is horrified story, version 2 is neutral story.

---

## 5. Data Analysis Pipeline

Questionnaires are stored in `data_analysis` folder.

The user study analysis uses two Google Forms CSV files:

```text
User Study Pre.csv
User Study Post.csv
```

These files should be placed in the `data_analysis` folder or in the same working directory used to run the scripts.

---

### Main analysis script

The recommended script is:

```text
analyse_user_study_csv_style_prepost_only_v2.py
```

Run:

```bash
python code/analyse_user_study_csv_style_prepost_only_v2.py
```

This script:

```text
1. Loads the pre-game and post-game CSV files.
2. Matches participants by nickname.
3. Separates participants by game version.
4. Calculates PANAS, SAM / Manikin, GUESS, and horror-level scores.
5. Runs statistical tests.
6. Exports CSV result tables.
```

Main output files:

```text
complete_statistical_analysis.csv
score_table.csv
participant_version_table.csv
```

The script uses:

```text
Wilcoxon signed-rank test -> within-group pre-post comparison
Mann-Whitney U test      -> between-version comparison
```

---

### PANAS-only analysis

Run:

```bash
python code/panas_prepost_only.py
```

Output files:

```text
panas_individual_scores.csv
panas_statistical_analysis.csv
```

Use this script if you only want to check the PANAS results.

---

### GUESS and horror-level analysis

Run:

```bash
python code/guess_annoyance_prepost_only.py
```

Output files:

```text
guess_individual_scores.csv
guess_statistical_analysis.csv
horror_level_individual_scores.csv
horror_level_statistical_analysis.csv
```

This script analyses post-game GUESS scores and the horror-level rating.

---



