# convert_story_to_json.py
# Convert a visual novel story text package into Unity-readable VN JSON.
# Input: plain text story package
# Output: JSON file

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

MAX_OUTPUT_TOKENS = 12000

DEFAULT_PROMPT_PATH = "prompt/convert_json.txt"
DEFAULT_INPUT_STORY_PATH = "outputs/story_neutral_text.txt"
DEFAULT_OUTPUT_JSON_PATH = "outputs/story_neutral22.json"


# =========================
# tool function
# =========================

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
    """
    Try to extract valid JSON from model output.
    This handles cases where the model accidentally wraps JSON in markdown.
    """
    text = text.strip()

    # Remove ```json ... ``` or ``` ... ```
    if text.startswith("```"):
        text = re.sub(r"^```(?:json)?", "", text.strip(), flags=re.IGNORECASE).strip()
        text = re.sub(r"```$", "", text.strip()).strip()

    # Try direct parse first
    try:
        json.loads(text)
        return text
    except json.JSONDecodeError:
        pass

    # Fallback: find first { and last }
    start = text.find("{")
    end = text.rfind("}")

    if start == -1 or end == -1 or end <= start:
        raise ValueError("No JSON object found in model output.")

    candidate = text[start:end + 1].strip()

    # Validate
    json.loads(candidate)
    return candidate


def validate_vn_json(data: dict) -> None:
    """
    Basic validation for your VN JSON.
    It checks top-level fields, scene ids, next, and gotoScene.
    """
    required_top_fields = ["title", "version", "language", "scenes"]

    for field in required_top_fields:
        if field not in data:
            raise ValueError(f"Missing top-level field: {field}")

    if not isinstance(data["scenes"], list):
        raise ValueError("'scenes' must be a list.")

    scene_ids = set()

    required_scene_fields = [
        "id",
        "chapter",
        "phase",
        "bg",
        "leftCharacter",
        "rightCharacter",
        "time",
        "light",
        "sfx",
        "bgm",
        "narration",
        "dialogue",
        "actions",
        "choices",
        "next",
    ]

    for scene in data["scenes"]:
        for field in required_scene_fields:
            if field not in scene:
                raise ValueError(
                    f"Scene is missing field '{field}'. Scene: {scene.get('id', 'UNKNOWN')}"
                )

        scene_id = scene["id"]

        if not scene_id:
            raise ValueError("A scene has an empty id.")

        if scene_id in scene_ids:
            raise ValueError(f"Duplicate scene id found: {scene_id}")

        scene_ids.add(scene_id)

    for scene in data["scenes"]:
        scene_id = scene["id"]

        # Validate next
        next_id = scene.get("next")
        if next_id is not None and next_id not in scene_ids:
            raise ValueError(
                f"Invalid next target in scene '{scene_id}': {next_id}"
            )

        # Validate choices
        choices = scene.get("choices", [])
        if choices is None:
            choices = []

        if not isinstance(choices, list):
            raise ValueError(f"'choices' must be a list in scene '{scene_id}'.")

        for choice in choices:
            if "text" not in choice or "gotoScene" not in choice:
                raise ValueError(f"Invalid choice format in scene '{scene_id}'.")

            goto_id = choice["gotoScene"]
            if goto_id not in scene_ids:
                raise ValueError(
                    f"Invalid gotoScene target in scene '{scene_id}': {goto_id}"
                )

    print("JSON validation passed.")


def convert_story_to_json(
    prompt_path: str = DEFAULT_PROMPT_PATH,
    input_story_path: str = DEFAULT_INPUT_STORY_PATH,
    output_json_path: str = DEFAULT_OUTPUT_JSON_PATH,
    model: str = MODEL_NAME,
    max_output_tokens: int = MAX_OUTPUT_TOKENS,
):
    converter_prompt = read_text(prompt_path)
    story_text = read_text(input_story_path)

    full_user_prompt = (
        converter_prompt
        + "\n\n"
        + "----- BEGIN STORY TEXT -----\n"
        + story_text
        + "\n----- END STORY TEXT -----\n"
    )

    client = OpenAI(api_key=OPENAI_API_KEY)

    response = client.responses.create(
        model=model,
        input=[
            {
                "role": "developer",
                "content": (
                    "You are a strict JSON converter for a Unity visual novel system. "
                    "Return only valid JSON. "
                    "Do not include markdown. "
                    "Do not include explanations. "
                    "Preserve the story content and only structure it."
                ),
            },
            {
                "role": "user",
                "content": full_user_prompt,
            },
        ],
        max_output_tokens=max_output_tokens,
    )

    raw_output = response.output_text.strip()

    json_text = extract_json(raw_output)
    data = json.loads(json_text)

    validate_vn_json(data)

    # Pretty save
    formatted_json = json.dumps(data, ensure_ascii=False, indent=2)
    save_text(output_json_path, formatted_json)

    print("=" * 60)
    print("Story converted to JSON successfully.")
    print(f"Model used: {model}")
    print(f"Input story: {input_story_path}")
    print(f"Output JSON: {output_json_path}")
    print(f"Number of scenes: {len(data.get('scenes', []))}")
    print("=" * 60)

    return data


def main():
    parser = argparse.ArgumentParser(
        description="Convert visual novel story text into Unity-readable JSON."
    )

    parser.add_argument(
        "--prompt",
        default=DEFAULT_PROMPT_PATH,
        help="Path to the story-to-JSON converter prompt.",
    )

    parser.add_argument(
        "--input",
        default=DEFAULT_INPUT_STORY_PATH,
        help="Path to the input story text file.",
    )

    parser.add_argument(
        "--out",
        default=DEFAULT_OUTPUT_JSON_PATH,
        help="Path to save the output JSON file.",
    )

    parser.add_argument(
        "--model",
        default=MODEL_NAME,
        help="Model name. This overrides MODEL_NAME in the script.",
    )

    parser.add_argument(
        "--max-output-tokens",
        type=int,
        default=MAX_OUTPUT_TOKENS,
        help="Maximum output tokens for the generated JSON.",
    )

    args = parser.parse_args()

    convert_story_to_json(
        prompt_path=args.prompt,
        input_story_path=args.input,
        output_json_path=args.out,
        model=args.model,
        max_output_tokens=args.max_output_tokens,
    )


if __name__ == "__main__":
    main()