import os
import asyncio
import deepl

# Set the base directory relative to the script location
BASE_DIR = os.path.dirname(os.path.abspath(__file__))
# Define file paths
SYNONYMS_FILE_NAME = os.path.join(BASE_DIR, "ColpaBot", "Resources", "Synonyms", "synonyms")
QNA_FILE_NAME = os.path.join(BASE_DIR, "ColpaBot", "Resources", "QuestionsAndAnswers", "questions_and_answers")
ADDITIONAL_TEXT_FILE_NAME = os.path.join(BASE_DIR, "ColpaBot", "Resources", "bot_messages")
LANGUAGES_FILE_NAME = os.path.join(BASE_DIR, "ColpaBot", "Resources", "languages")
QNA_FILE_EXT = ".tsv"  # File extension for translation files
COMMENT_PREFIX = "//"  # Prefix used to mark comments in files

# Define the field separator based on the file extension
if QNA_FILE_EXT == ".tsv":
    SEPARATOR = '\t'
else:
    raise Exception(f"{QNA_FILE_EXT} is an invalid file extension. Only .tsv files are allowed")

# Read the languages file and skip comment lines
with open(f"{LANGUAGES_FILE_NAME}{QNA_FILE_EXT}", 'r', encoding='utf-8') as file:
    line = file.readline()
    while line.startswith(COMMENT_PREFIX):
        line = file.readline()
    languages_input = line.strip().split(SEPARATOR)

# Set the DeepL authentication key from a file
with open(os.path.join(BASE_DIR, "deepl_key.txt"), 'r') as file:
    key = file.read().strip()
translator = deepl.Translator(key)

# Ask the user for the base language
print("Please select the language you want to translate from:")
for i, lang in enumerate(languages_input):
    print(f"{i+1} {lang}")
BASE_LANG = languages_input[int(input())-1]
if BASE_LANG not in languages_input:
    raise Exception(f"Base language {BASE_LANG} not found in {ADDITIONAL_TEXT_FILE_NAME}{QNA_FILE_EXT}")

# Ask the user which files to translate
print("Do you want to translate the QnA files? (y/n)")
TRANSLATE_QNA = input().lower() == 'y'
print("Do you want to translate the Synonyms files? (y/n)")
TRANSLATE_SYNONYMS = input().lower() == 'y'
print("Do you want to translate the bot messages? (y/n)")
TRANSLATE_BOT_MESSAGES = input().lower() == 'y'
BOT_MESSAGES_CHANGED = False  # Re-translate all bot messages if True

# Extract the general language code (e.g., "EN" from "EN-GB")
def get_general_lang(lang):
    return lang.split("-")[0]

# Function to translate QnA files
async def translate_qna(source_lang, target_lang):
    if source_lang == target_lang:
        raise Exception(f"Source language {source_lang} is the same as target language {target_lang}. No translation needed.")
    if target_lang not in languages_input or source_lang not in languages_input:
        raise Exception(f"Language not found in {LANGUAGES_FILE_NAME}{QNA_FILE_EXT}")
    
    source_general_lang = get_general_lang(source_lang)
    source_file = f"{QNA_FILE_NAME}_{source_lang}{QNA_FILE_EXT}"
    target_file = f"{QNA_FILE_NAME}_{target_lang}{QNA_FILE_EXT}"

    try:
        with open(source_file, 'r', encoding='utf-8') as reader, open(target_file, 'w', encoding='utf-8') as writer:
            print(f"Translating {source_file} from {source_lang} into {target_lang} in {target_file}...")
            writer.write(next(reader)) # skip header
            no_translation_string = next(reader)
            writer.write(no_translation_string)

            for line in reader:
                fields = line.split(SEPARATOR)
                
                if len(line.strip().strip("\n")) == 0 or fields[0].strip().strip("\n").lstrip("-").isdigit() or line.startswith(COMMENT_PREFIX): # skip empty lines, numbers and comments
                    writer.write(line)
                    continue
                
                if len(fields) != 3: # expects only 2 columns
                    print(fields)
                    raise Exception(f"Invalid file for translation: {QNA_FILE_NAME}. It has {len(fields)} columns when there should be 3\nColumns: {fields}")
                
                result = [fields[0]]  # Keep the first column unchanged
                for text in fields[1:]:
                    if text == no_translation_string:
                        result.append(text)
                        continue
                    translation = translator.translate_text(text, source_lang=source_general_lang, target_lang=target_lang)
                    if isinstance(translation, list):
                        result.extend([t.text for t in translation])
                    else:
                        result.append(translation.text)
                        
                writer.write(SEPARATOR.join(result))
            print("QnA translation succesfully finished!")
    except Exception as e:
        if isinstance(e, FileNotFoundError):
            print(f"Error: File {source_file} not found")
        else:
            raise Exception(f"Unexpected error: {str(e)}")

# Function to translate synonyms files
async def translate_synonyms(source_lang, target_lang):
    if source_lang == target_lang:
        raise Exception(f"Source language {source_lang} is the same as target language {target_lang}. No translation needed.")
    if target_lang not in languages_input:
        raise Exception(f"Target language {target_lang} not found in {LANGUAGES_FILE_NAME}{QNA_FILE_EXT}")
    if source_lang not in languages_input:
        raise Exception(f"Source language {source_lang} not found in {LANGUAGES_FILE_NAME}{QNA_FILE_EXT}")
    
    source_general_lang = get_general_lang(source_lang)
    source_file = f"{SYNONYMS_FILE_NAME}_{source_lang}{QNA_FILE_EXT}"
    target_file = f"{SYNONYMS_FILE_NAME}_{target_lang}{QNA_FILE_EXT}"

    try:
        with open(source_file, 'r', encoding='utf-8') as reader, open(target_file, 'w', encoding='utf-8') as writer:
            print(f"Translating {source_file} from {source_lang} into {target_lang} in {target_file}...")
            for line in reader:
                fields = line.split(SEPARATOR)
                
                if len(line.strip().strip("\n")) == 0 or line.startswith(COMMENT_PREFIX): # skip empty lines, numbers and comments
                    writer.write(line)
                    continue
                
                if len(fields) < 2: # expects more than 1 columns
                    print(fields)
                    raise Exception(f"Expected at least 2 columns in {SYNONYMS_FILE_NAME}, but got {len(fields)}\nColumns: {fields}")
                
                result = []
                for text in fields:
                    translation = translator.translate_text(text, source_lang=source_general_lang, target_lang=target_lang)
                    if isinstance(translation, list):
                        result.extend([t.text for t in translation])
                    else:
                        result.append(translation.text)
                        
                writer.write(SEPARATOR.join(result))
            print("Synonyms translation succesfully finished!")
    except Exception as e:
        if isinstance(e, FileNotFoundError):
            print(f"Error: File {source_file} not found")
        else:
            raise Exception(f"Unexpected error: {str(e)}")

# Function to translate bot messages
async def translate_bot_messages():
    input_file = f"{ADDITIONAL_TEXT_FILE_NAME}{QNA_FILE_EXT}"
    general_base_lang = get_general_lang(BASE_LANG)
    try:
        with open(input_file, 'r', encoding='utf-8') as reader:
            file_in_memory_iter = iter(reader.readlines())
        with open(input_file, 'w', encoding='utf-8') as writer:
            # Read header to get target languages
            writer.write(next(file_in_memory_iter)) # skip the first line {}
            writer.write(next(file_in_memory_iter)) # skip the second line {{}}
            header = next(file_in_memory_iter)
            writer.write(header)
            target_langs = header.strip().split(SEPARATOR)[1:]  # Skip the first column (langSelection)
            
            target_langs = [BASE_LANG] + [lang for lang in target_langs if lang != BASE_LANG] # place english as first
            if target_langs != languages_input:
                raise Exception(f"Languages in {ADDITIONAL_TEXT_FILE_NAME} do not match those in {LANGUAGES_FILE_NAME}")
            
            for line in file_in_memory_iter:
                line = line.strip()
                # Skip empty lines
                if not line:
                    continue

                fields = line.split(SEPARATOR)
       
                if len(line) == 0: # skip empty lines
                    continue
                if (not BOT_MESSAGES_CHANGED) and len(fields) == len(target_langs) + 1: # skip translated lines
                    writer.write(line + '\n')
                    continue
                
                # Extract the key (e.g., langSelection)
                key = fields[0]

                # Translate each field except the first one (key)
                text = fields[1]
                translated_fields = [text]
                
                for i in range(1, len(target_langs)):
                    translation = translator.translate_text(text, source_lang=general_base_lang, target_lang=target_langs[i])
                    translated_fields.append(translation.text)

                writer.write(SEPARATOR.join(translated_fields) + '\n')

            print("Translation successfully finished!")
    except FileNotFoundError as e:
        print(f"Error: File {input_file} not found")
    except Exception as e:
        raise Exception(f"Unexpected error: {str(e)}")

# Translate selected files
if TRANSLATE_QNA:
    for lang in languages_input:
        if lang != BASE_LANG:
            asyncio.run(translate_qna(BASE_LANG, lang))
if TRANSLATE_SYNONYMS:
    for lang in languages_input:
        if lang != BASE_LANG:
            asyncio.run(translate_synonyms(BASE_LANG, lang))
if TRANSLATE_BOT_MESSAGES:
    asyncio.run(translate_bot_messages())

# python translate.py