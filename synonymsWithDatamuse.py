import os
import re
import requests

GET_SYNONYMS = True
GROUP_SYNONYMS = False  # Flag to control grouping of synonyms: if there are 2 lines with the same synonym, they will be grouped into one line
LANGUAGE = "EN-GB"
COMMENT_CHARS = "//"

# Set the base directory relative to the script's location
BASE_DIR = os.path.dirname(os.path.abspath(__file__))
SYNONYMS_DIR = os.path.join(BASE_DIR, "ColpaBot", "Resources", "Synonyms")
WORDS_DIR = os.path.join(BASE_DIR, "ColpaBot", "Resources", "QuestionsAndAnswers")

class DatamuseService:
    BASE_URL = 'https://api.datamuse.com/words'

    # Fetch synonyms for a word from the Datamuse API
    def get_synonyms(self, word, min_score=500, max_results=10):
        response = requests.get(self.BASE_URL, params={'rel_syn': word})
        response.raise_for_status()  # Raise an error for bad responses
        data = response.json()
        # Filter synonyms based on score and limit results
        filtered_synonyms = [item['word'] for item in data if 'score' in item and item['score'] > min_score]
        top_synonyms = filtered_synonyms[:max_results]
        return top_synonyms
    
# Group synonyms in the file by merging similar entries
def group_synonyms(filename):
    # Read the file and split lines into synonym lists
    with open(os.path.join(SYNONYMS_DIR, filename), 'r', encoding='utf-8') as file:
        lines = file.readlines()
    # Set separator based on file type
    if filename.endswith(".csv"):
        separator = ","
    elif filename.endswith(".tsv"):
        separator = "\t"
    else:
        raise Exception(f"{filename} is an invalid file extension. Only .csv and .tsv files are allowed")

    synonymsByLine = list(map(lambda line: line.strip().lower().split(separator), lines))
    changes = True

    # Replace a synonym with a group of synonyms in the list
    def substitute_synonyms(synonym_to_be_substituted, synonyms_to_group, index_of_synonyms_to_group):
        for i, line in enumerate(synonymsByLine):
            if synonym_to_be_substituted in line:
                synonymsByLine[i] += synonyms_to_group
                synonymsByLine[index_of_synonyms_to_group] = []  # Clear the processed line
                return
        raise Exception(f"Synonym {synonym_to_be_substituted} not found in the file")
     
    # Loop until no changes are needed
    while changes:
        changes = False
        processedSynonyms = set()
        for i, line in enumerate(synonymsByLine):
            if not line or len(line) < 2:  # Skip empty or single-word lines
                changes = True
                synonymsByLine.pop(i)
                continue
            length = len(line)
            line = list(set(line))  # Remove duplicate synonyms within a line
            if len(line) != length:
                changes = True
            synonymsByLine[i] = line
            for synonym in line:
                if synonym in processedSynonyms:
                    substitute_synonyms(synonym, line, i)
                    changes = True
                    processedSynonyms = processedSynonyms.union(line)
                    break
                processedSynonyms.add(synonym)

    # Write the updated synonym groups back to the file
    with open(os.path.join(SYNONYMS_DIR, filename), 'w', encoding='utf-8') as file:
        for line in synonymsByLine:
            file.write(separator.join(line) + "\n")

# Normalize text by removing/replacing punctuation and converting to lowercase
def normalize(text):
    PUNCTUATION_TO_REPLACE_WITH_SPACE = r"[\.,;¡!¿?\"\-\+\*\|@#$~%&=\\\/<>(){}\[\]]"
    PUNCTUATION_TO_REMOVE = r"['’`´¨^·]"
    text = re.sub(PUNCTUATION_TO_REMOVE, "", text)
    text = re.sub(PUNCTUATION_TO_REPLACE_WITH_SPACE, " ", text)
    text = re.sub(r"\s+", " ", text)  # Replace multiple spaces with a single space
    return text.strip().lower()  # Trim spaces and convert to lowercase

if GET_SYNONYMS:
    datamuse_service = DatamuseService()

    # Read questions from the file and extract words from the questions column
    with open(os.path.join(WORDS_DIR, f"questions_and_answers_{LANGUAGE}.tsv"), 'r', encoding='utf-8') as file:
        content = file.readlines()
    ignoreSymbols = [line.removesuffix("\n") for line in content[:2]]
    # Extract second column (questions) for each valid line
    questions = [line.split('\t')[1] for line in content if not line.startswith(COMMENT_CHARS) and not all(char in ['\n', ' ', '\r'] for char in line) and len(line.split('\t')) > 1]
    words = {normalize(word) for word in " ".join(questions).split()}
    # Remove empty words or words containing only whitespace
    words = {word for word in words if word.strip() and len(word) > 0}

    # Remove ignored symbols from words
    for s in ignoreSymbols:
        if s in words:
            words.remove(s)

    contentToWrite = ""
    # Fetch synonyms for each word and prepare the output
    for word in words:
        synonyms = datamuse_service.get_synonyms(word)
        contentToWrite += word + "\t" + "\t".join(synonyms) + "\n"

    # Write the synonyms to a new file
    with open(os.path.join(SYNONYMS_DIR, f"synonyms_{LANGUAGE}.tsv"), 'w', encoding='utf-8') as file:
        file.write(contentToWrite)
        print("Synonyms written to " + file.name)

if GROUP_SYNONYMS:
    # Group synonyms in all valid files in the synonyms directory
    for filename in os.listdir(SYNONYMS_DIR):
        if filename.endswith(".csv") or filename.endswith(".tsv"):
            group_synonyms(filename)
            print(f"Synonyms in {filename} have been grouped")
        else:
            print(f"{filename} is an invalid file extension. Only .csv and .tsv files are allowed")