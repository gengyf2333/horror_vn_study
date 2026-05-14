import argparse
import json
import re
from pathlib import Path
from openai import OpenAI

# =========================
# use your own api key and settings
# =========================

OPENAI_API_KEY = "sk-proj-xxxxxxxx"
MODEL_NAME = "gpt-5.5"

DEFAULT_PROMPT_PATH = "prompt/image_list.txt"
DEFAULT_INPUT_JSON_PATH = "outputs/story_neutral.json"
DEFAULT_OUTPUT_PATH = "outputs/asset_prompts_neutral.json"

MAX_OUTPUT_TOKENS = 10000


def read_text(path: str) -> str:
    path_obj = Path(path)
    if not path_obj.exists():
        raise FileNotFoundError(f"File not found: {path}")
    return path_obj.read_text(encoding="utf-8")


def save_text(path: str, text: str) -> None:
    path_obj = Path(path)
    path_obj.parent.mkdir(parents=True, exist_ok=True)
    path_obj.write_text(text, encoding="utf-8")


def extract_json(text: str) -> str:
    text = text.strip()

    if text.startswith("```"):
        text = re.sub(r"^```(?:json)?", "", text, flags=re.IGNORECASE).strip()
        text = re.sub(r"```$", "", text).strip()

    try:
        json.loads(text)
        return text
    except json.JSONDecodeError:
        pass

    start = text.find("{")
    end = text.rfind("}")

    if start == -1 or end == -1 or end <= start:
        raise ValueError("No JSON object found in model output.")

    candidate = text[start:end + 1].strip()
    json.loads(candidate)
    return candidate


def summarize_assets_from_vn_json(vn_data: dict) -> dict:
    scenes = vn_data.get("scenes", [])

    bg_map = {}
    char_map = {}

    for scene in scenes:
        scene_id = scene.get("id", "")

        bg = scene.get("bg", "")
        if bg:
            bg_map.setdefault(bg, []).append(scene_id)

        for char_id in [scene.get("leftCharacter", ""), scene.get("rightCharacter", "")]:
            if char_id:
                char_map.setdefault(char_id, []).append(scene_id)

    return {
        "title": vn_data.get("title", ""),
        "version": vn_data.get("version", ""),
        "background_ids": [
            {"asset_id": bg_id, "source_scene_ids": scene_ids}
            for bg_id, scene_ids in sorted(bg_map.items())
        ],
        "character_sprite_ids": [
            {"asset_id": char_id, "source_scene_ids": scene_ids}
            for char_id, scene_ids in sorted(char_map.items())
        ],
        "full_json": vn_data
    }


def generate_asset_prompts(prompt_path: str, input_json_path: str, output_path: str, mode: str, model: str):
    prompt_template = read_text(prompt_path)
    vn_json_text = read_text(input_json_path)
    vn_data = json.loads(vn_json_text)

    asset_summary = summarize_assets_from_vn_json(vn_data)
    compact_input = json.dumps(asset_summary, ensure_ascii=False, indent=2)

    full_prompt = (
        prompt_template
        .replace("{{MODE}}", mode)
        .replace("{{VN_JSON}}", compact_input)
    )

    client = OpenAI(api_key=OPENAI_API_KEY)

    response = client.responses.create(
        model=model,
        input=[
            {
                "role": "developer",
                "content": (
                    "You are a strict visual novel asset prompt generator. "
                    "Return only valid JSON. "
                    "Do not rename asset IDs. "
                    "Do not invent new assets."
                ),
            },
            {
                "role": "user",
                "content": full_prompt,
            },
        ],
        max_output_tokens=MAX_OUTPUT_TOKENS,
    )

    raw_output = response.output_text.strip()
    json_text = extract_json(raw_output)
    data = json.loads(json_text)

    formatted = json.dumps(data, ensure_ascii=False, indent=2)
    save_text(output_path, formatted)

    print("=" * 60)
    print("Asset prompts generated successfully.")
    print(f"Mode: {mode}")
    print(f"Input JSON: {input_json_path}")
    print(f"Output: {output_path}")
    print(f"Background prompts: {len(data.get('backgrounds', []))}")
    print(f"Character prompts: {len(data.get('characters', []))}")
    print("=" * 60)


def main():
    parser = argparse.ArgumentParser()

    parser.add_argument("--prompt", default=DEFAULT_PROMPT_PATH)
    parser.add_argument("--input", default=DEFAULT_INPUT_JSON_PATH)
    parser.add_argument("--out", default=DEFAULT_OUTPUT_PATH)
    parser.add_argument("--mode", choices=["neutral", "horrorified"], required=True)
    parser.add_argument("--model", default=MODEL_NAME)

    args = parser.parse_args()

    generate_asset_prompts(
        prompt_path=args.prompt,
        input_json_path=args.input,
        output_path=args.out,
        mode=args.mode,
        model=args.model,
    )


if __name__ == "__main__":
    main()