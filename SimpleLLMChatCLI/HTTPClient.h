#pragma once
#include <string>
#include <winsock2.h>
#include <ws2tcpip.h>

class HttpClient {
public:
    HttpClient(const char* host, const char* port);
    ~HttpClient();

    bool connect();
    bool sendAll(const std::string& data);
    bool recvLine(std::string& line);
    bool recvN(char* buf, int len);
	const std::string& getHost() const { return host; }
	const std::string& getPort() const { return port; }

private:
    std::string host, port;
    SOCKET sock;
};