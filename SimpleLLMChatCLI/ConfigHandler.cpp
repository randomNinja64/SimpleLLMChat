#include "ConfigHandler.h"
#include <fstream>
#include <iostream>

ConfigHandler::ConfigHandler(const std::string& filename) {
    loadConfig(filename);
}

void ConfigHandler::loadConfig(const std::string& filename) {
    std::ifstream file(filename);
    if (!file.is_open()) {
        std::cerr << "Failed to open config file: " << filename << "\n";
        return;
    }

    std::string line;
    while (std::getline(file, line)) {
        if (line.empty() || line[0] == '#') continue;
        auto pos = line.find('=');
        if (pos != std::string::npos) {
            std::string key = line.substr(0, pos);
            std::string value = line.substr(pos + 1);
            configMap[key] = value;
        }
    }
}

std::string ConfigHandler::getHost() const {
    return configMap.count("host") ? configMap.at("host") : "127.0.0.1";
}

std::string ConfigHandler::getPort() const {
    return configMap.count("port") ? configMap.at("port") : "80";
}

std::string ConfigHandler::getApiKey() const {
    return configMap.count("apiKey") ? configMap.at("apiKey") : "";
}

std::string ConfigHandler::getModel() const {
    return configMap.count("model") ? configMap.at("model") : "";
}

std::string ConfigHandler::getSysPrompt() const {
    return configMap.count("sysprompt") ? configMap.at("sysprompt") : "";
}

std::string ConfigHandler::getAssistantName() const {
    return configMap.count("assistantname") ? configMap.at("assistantname") : "";
}