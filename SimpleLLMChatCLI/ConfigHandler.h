#pragma once
#include <string>
#include <map>

class ConfigHandler {
public:
    ConfigHandler(const std::string& filename = "LLMSettings.ini");

    std::string getHost() const;
    std::string getPort() const;
    std::string getApiKey() const;
	std::string getModel() const;
	std::string getSysPrompt() const;
	std::string getAssistantName() const;

private:
    std::map<std::string, std::string> configMap;
    void loadConfig(const std::string& filename);
};
