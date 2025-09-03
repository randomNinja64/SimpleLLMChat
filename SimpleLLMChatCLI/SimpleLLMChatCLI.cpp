#include "LLMClient.h"
#include "ConfigHandler.h"
#include "Base64Image.h"
#include <iostream>
#include <string>
#include <vector>
#include <utility> // for std::pair

// Process conversation: add user input, send to LLM, store assistant response
void processConversation(LLMClient& client,
                         std::vector<std::pair<std::string, std::string> >& conversation,
                         const std::string& userMessage,
                         const std::vector<std::string>& images,
                         const std::string& assistantName)
{
    // Store user message
    conversation.push_back(std::make_pair("User", userMessage));

    // Build prompt including conversation history (VS2010-compatible)
    std::string fullPrompt;
    for (size_t i = 0; i < conversation.size(); ++i) {
        fullPrompt += conversation[i].first + ": " + conversation[i].second + "\n";
    }
    fullPrompt += "Assistant: ";

    // Print assistant label before streaming
    std::cout << "\n" << assistantName << ": ";

    // Send prompt to LLM (with or without images)
    std::string response = client.sendPrompt(fullPrompt, images);

    // Store assistant response
    conversation.push_back(std::make_pair("Assistant", response));
}

int main(int argc, char* argv[])
{
    // Load configuration
    ConfigHandler config;
    LLMClient client(
        config.getHost(),
        config.getPort(),
        config.getApiKey(),
        config.getModel(),
        config.getSysPrompt()
    );

    std::vector<std::pair<std::string, std::string> > conversation;

    // -------------------
    // Command-line argument handling
    // -------------------
    if (argc > 1) {
        std::vector<std::string> images;
        std::string textPrompt;

        int argIndex = 1;
        if (std::string(argv[argIndex]) == "--image" && argc > argIndex + 1) {
            std::string imagePath = argv[argIndex + 1];

            try {
                std::string base64Image = imageFileToBase64(imagePath);
                images.push_back(base64Image);
            } catch (const std::exception& e) {
                std::cerr << "Error processing image: " << e.what() << std::endl;
                return 1;
            }

            // Remaining arguments are treated as prompt
            textPrompt = "";
            for (int i = argIndex + 2; i < argc; ++i) {
                textPrompt += argv[i];
                if (i < argc - 1) textPrompt += " ";
            }
        } else {
            // All arguments are treated as prompt
            textPrompt = "";
            for (int i = argIndex; i < argc; ++i) {
                textPrompt += argv[i];
                if (i < argc - 1) textPrompt += " ";
            }
        }

        // Process conversation once and exit
        processConversation(client, conversation, textPrompt, images, config.getAssistantName());
        return 0;
    }

    // -------------------
    // Interactive mode
    // -------------------
    std::cout << "=== SimpleLLMChat CLI ===\n";
    std::cout << "Type 'exit' to quit.\n";
    std::cout << "Type 'clear' to reset the chat.\n";
    std::cout << "Type 'image <filepath>' to send an image.\n";

    while (true) {
        std::string userInput;
        std::cout << "\nYou: ";
        std::getline(std::cin, userInput);

        if (userInput == "exit") break;

        if (userInput == "clear") {
            std::cout << "Context cleared.\n";
            conversation.clear();
            std::cout << "=== SimpleLLMChat CLI ===\n";
            std::cout << "Type 'exit' to quit.\n";
            std::cout << "Type 'clear' to reset the chat.\n";
            std::cout << "Type 'image <filepath>' to send an image.\n";
            continue;
        }

        std::vector<std::string> images;
        std::string textPrompt;

        // Check for "image " command
        if (userInput.length() > 6 && userInput.substr(0, 6) == "image ") {
            size_t quoteStart = userInput.find('"', 6);
            size_t quoteEnd = userInput.find('"', quoteStart + 1);

            if (quoteStart == std::string::npos || quoteEnd == std::string::npos) {
                std::cerr << "Error: Please enclose the image path in quotes.\n";
                continue;
            }

            std::string imagePath = userInput.substr(quoteStart + 1, quoteEnd - quoteStart - 1);

            // Remaining text after the closing quote
            if (quoteEnd + 1 < userInput.length())
                textPrompt = userInput.substr(quoteEnd + 1);
            else
                textPrompt = "";

            // Trim leading spaces from the prompt
            size_t firstNonSpace = textPrompt.find_first_not_of(" \t");
            if (firstNonSpace != std::string::npos)
                textPrompt = textPrompt.substr(firstNonSpace);
            else
                textPrompt = "";

            try {
                std::string base64Image = imageFileToBase64(imagePath);
                images.push_back(base64Image);
                //std::cout << "Image converted to base64 successfully.\n";
            } catch (const std::exception& e) {
                std::cerr << "Error processing image: " << e.what() << std::endl;
                continue;
            }

        } else {
            // Regular text message
            textPrompt = userInput;
        }

        // Process conversation with optional image(s)
        processConversation(client, conversation, textPrompt, images, config.getAssistantName());
    }

    return 0;
}
