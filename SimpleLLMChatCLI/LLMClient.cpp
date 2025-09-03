#include "LLMClient.h"
#include <sstream>
#include <iostream>

LLMClient::LLMClient(const std::string& host,
                     const std::string& port,
                     const std::string& key,
                     const std::string& mdl,
                     const std::string& sysprompt)
    : http(host.c_str(), port.c_str()),
      apiKey(key),
      model(mdl),
      systemPrompt(sysprompt)
{}

// Send prompt with optional images
std::string LLMClient::sendPrompt(const std::string& prompt, const std::vector<std::string>& images) {
    if (!http.connect()) {
        std::cerr << "Connection failed\n";
        return "";
    }

    std::ostringstream body;
    body << "{";
    body << "\"model\":\"" << jsonEscape(model) << "\",";
    body << "\"messages\":[";

    // System message
    body << "{"
         << "\"role\":\"system\","
         << "\"content\":[{\"type\":\"text\",\"text\":\"" << jsonEscape(systemPrompt) << "\"}]"
         << "},";

    // User message
    body << "{"
         << "\"role\":\"user\","
         << "\"content\":[";

    bool first = true;

    // Add user text if present
    if (!prompt.empty()) {
        body << "{\"type\":\"text\",\"text\":\"" << jsonEscape(prompt) << "\"}";
        first = false;
    }

    // Add images (as data URLs)
    for (size_t i = 0; i < images.size(); ++i) {
    if (!first) body << ",";
    const std::string& img_b64 = images[i];

    body << "{"
         << "\"type\":\"image_url\","
         << "\"image_url\":{"
         << "\"url\":\"data:image/png;base64," << jsonEscape(img_b64) << "\""
         << "}"
         << "}";

    first = false;
}

    body << "]"
         << "}";  // end user message
    body << "],";  // end messages array
    body << "\"stream\":true";
    body << "}";

    std::string bodyStr = body.str();

    // Compose HTTP request
    std::ostringstream req;
    req << "POST /v1/chat/completions HTTP/1.1\r\n";
    req << "Host: " << http.getHost() << "\r\n";
    req << "Content-Type: application/json\r\n";
    if (!apiKey.empty())
        req << "Authorization: Bearer " << apiKey << "\r\n";
    req << "Content-Length: " << bodyStr.size() << "\r\n";
    req << "Connection: close\r\n\r\n";
    req << bodyStr;

    if (!http.sendAll(req.str())) {
        std::cerr << "Failed to send request\n";
        return "";
    }

    // Stream response
    std::string output;
    std::string line;
    while (http.recvLine(line)) {
        if (line.find("data: ") == 0) {
            std::string jsonPart = line.substr(6);
            if (jsonPart == "[DONE]") break;

            size_t pos = jsonPart.find("\"content\":\"");
            if (pos != std::string::npos) {
                pos += 11;
                size_t end = jsonPart.find("\"", pos);
                if (end != std::string::npos) {
                    std::string chunk = jsonUnescape(jsonPart.substr(pos, end - pos));
                    std::cout << chunk << std::flush;
                    output += chunk;
                }
            }
        }
    }

    std::cout << std::endl;
    return output;
}

// Text-only convenience method
std::string LLMClient::sendPrompt(const std::string& prompt) {
    std::vector<std::string> noImages;
    return sendPrompt(prompt, noImages);
}
