#pragma once
#include <string>
#include <vector>
#include <iostream>
#include "HttpClient.h"  // must define HttpClient before using
#include "JsonUtil.h"

class LLMClient {
public:
    LLMClient(const std::string& host,
              const std::string& port,
              const std::string& key,
              const std::string& model,
              const std::string& sysPrompt);

    std::string sendPrompt(const std::string& prompt, const std::vector<std::string>& images);
    std::string sendPrompt(const std::string& prompt);

private:
    std::string apiKey;
    std::string model;
    std::string systemPrompt;
    HttpClient http;   // keep as value, not pointer
};
