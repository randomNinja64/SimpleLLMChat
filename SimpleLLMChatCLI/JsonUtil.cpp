#include "JsonUtil.h"
#include <sstream>

std::string jsonEscape(const std::string& s) {
    std::ostringstream oss;
    for (size_t i = 0; i < s.size(); ++i) {
        char c = s[i];
        if (c == '\\' || c == '"') oss << '\\' << c;
        else if (c == '\n') oss << "\\n";
        else if (c == '\r') oss << "\\r";
        else if (c == '\t') oss << "\\t";
        else oss << c;
    }
    return oss.str();
}

std::string jsonUnescape(const std::string& s) {
    std::string out;
    for (size_t i = 0; i < s.size(); ++i) {
        if (s[i] == '\\' && i + 1 < s.size()) {
            ++i;
            if (s[i] == 'n') out += '\n';
            else if (s[i] == 'r') out += '\r';
            else if (s[i] == 't') out += '\t';
            else out += s[i];
        } else {
            out += s[i];
        }
    }
    return out;
}