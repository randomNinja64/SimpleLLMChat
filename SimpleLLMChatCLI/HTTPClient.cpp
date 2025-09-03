#include "HttpClient.h"
#include <iostream>

HttpClient::HttpClient(const char* h, const char* p) : host(h), port(p), sock(INVALID_SOCKET) {
    WSADATA wsa;
    WSAStartup(MAKEWORD(2,2), &wsa);
}

HttpClient::~HttpClient() {
    if (sock != INVALID_SOCKET) closesocket(sock);
    WSACleanup();
}

bool HttpClient::connect() {
    struct addrinfo hints = {0}, *res = 0;
    hints.ai_family = AF_INET;
    hints.ai_socktype = SOCK_STREAM;
    if (getaddrinfo(host.c_str(), port.c_str(), &hints, &res) != 0) return false;
    sock = socket(res->ai_family, res->ai_socktype, res->ai_protocol);
    if (::connect(sock, res->ai_addr, (int)res->ai_addrlen) != 0) return false;
    freeaddrinfo(res);
    return true;
}

bool HttpClient::sendAll(const std::string& data) {
    int sent = 0, len = (int)data.size();
    while (sent < len) {
        int n = send(sock, data.c_str() + sent, len - sent, 0);
        if (n <= 0) return false;
        sent += n;
    }
    return true;
}

bool HttpClient::recvLine(std::string& line) {
    line.clear();
    char c;
    while (true) {
        int n = recv(sock, &c, 1, 0);
        if (n <= 0) return false;
        if (c == '\r') continue;
        if (c == '\n') break;
        line += c;
    }
    return true;
}

bool HttpClient::recvN(char* buf, int len) {
    int recvd = 0;
    while (recvd < len) {
        int n = recv(sock, buf + recvd, len - recvd, 0);
        if (n <= 0) return false;
        recvd += n;
    }
    return true;
}
